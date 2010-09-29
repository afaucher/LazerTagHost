using System;
using System.IO.Ports;
using System.Globalization;
using System.Collections.Generic;

namespace LazerTagHost
{
	public class IRPacket {
		public enum PacketType {
			PACKET_TYPE_LTX,
			PACKET_TYPE_LTTO,
		};
		
		public PacketType packet_type;
		public UInt16 data;
		public UInt16 number_of_bits;
		
		public IRPacket(
		                PacketType packet_type,
			UInt16 data,
			UInt16 number_of_bits)
		{
			this.packet_type = packet_type;
			this.data = data;
			this.number_of_bits = number_of_bits;
		}
	}
	
	public class Player {
		public byte player_id;
		
		public Player(byte player_id) {
			this.player_id = player_id;
		}
	}
	
	class HostGun
	{
		private struct AddingState {
			public DateTime next_adv;
			public LinkedList<IRPacket> packets;
			public DateTime game_start_timeout;
		};
		
		private struct ConfirmJoinState {
			public byte player_id;
			public DateTime confirm_timeout;	
			public LinkedList<IRPacket> packets;
		};
		
		private struct CountdownState {
			public DateTime game_start;
			public DateTime last_tick;
		};
		
		private struct PlayingState {
			public DateTime game_end;
		};
		
		private struct SummaryState {
			public DateTime last_announce;
		};
		
		private enum HostingState {
			HOSTING_STATE_IDLE,
			HOSTING_STATE_ADDING,
			HOSTING_STATE_CONFIRM_JOIN,
			HOSTING_STATE_COUNTDOWN,
			HOSTING_STATE_PLAYING,
			HOSTING_STATE_SUMMARY,
		};
		
		private enum CommandCode {
			COMMAND_CODE_CUSTOM_GAME_MODE_HOST = 0x02,
			COMMAND_CODE_PLAYER_JOIN_GAME_REQUEST = 0x10,
			COMMAND_CODE_ACK_PLAYER_JOIN_RESPONSE = 0x01,
			COMMAND_CODE_CONFIRM_PLAY_JOIN_GAME = 0x11,
			COMMAND_CODE_COUNTDOWN_TO_GAME_START = 0x00,
			COMMAND_CODE_SCORE_ANNOUNCEMENT = 0x31,
			COMMAND_CODE_PLAYER_REPORT_SCORE = 0x40,
		};
		
		private byte game_id = 0x00;
		
		private SerialPort serial_port = null;
		private const int ADDING_ADVERTISEMENT_INTERVAL_SECONDS = 3;
		private const int WAIT_FOR_ADDITIONAL_PLAYERS_TIMEOUT_SECONDS = 60;
		private const int GAME_START_COUNTDOWN_INTERVAL_SECONDS = 30;
		private const int GAME_TIME_DURATION_MINUTES = 1;
		private const bool autostart = false;
		
		//host gun state
		private HostingState hosting_state = HostGun.HostingState.HOSTING_STATE_IDLE;
		private AddingState adding_state;
		private ConfirmJoinState confirm_join_state;
		private CountdownState countdown_state;
		private SummaryState summary_state;
		private PlayingState playing_state;
		private LinkedList<Player> players = new LinkedList<Player>();
		
		private void TransmitBytes(UInt16 data, UInt16 number_of_bits)
		{
			byte[] packet = new byte[2] {
				(byte)((number_of_bits << 4) | ((data >> 8) & 0xf)),
				(byte)(data & 0xff),
			};
			serial_port.Write( packet, 0, 2 );
			serial_port.BaseStream.Flush();
			string debug = String.Format("TX: {0:x}, {1:d}", new object[] {data, number_of_bits});
			MainClass.HostDebugWriteLine(debug);
			System.Threading.Thread.Sleep(100);
		}
		
		public HostGun(string device) {
			serial_port = new SerialPort(device, 115200);
			serial_port.Open();
			
			adding_state.packets = new LinkedList<IRPacket>();
			game_id = (byte)(new Random().Next());
			confirm_join_state.packets = new LinkedList<IRPacket>();
		}
		
		private bool ProcessPacket(IRPacket.PacketType type, UInt16 data, UInt16 number_of_bits)
		{
			DateTime now = DateTime.Now;
			
			switch (hosting_state) {
			case HostingState.HOSTING_STATE_ADDING:
			{
				adding_state.packets.AddLast(new IRPacket(type, data, number_of_bits));
				if (adding_state.packets.Count == 5) {
					//TODO Check Checksum
					IRPacket game_id_packet = adding_state.packets.First.Next.Value;
					IRPacket player_id_packet = adding_state.packets.First.Next.Next.Value;
					UInt16 player_id = player_id_packet.data;
					
					confirm_join_state.player_id = (byte)player_id;
					
					UInt16[][] values = new UInt16[][]{
						new UInt16[] {0x9,0x01},
						new UInt16[] {0x8,game_id},//Game ID
						new UInt16[] {0x8,player_id},//Player ID
						new UInt16[] {0x8,0x9}, //player #
						new UInt16[] {0x9,0x100},
					};
					ChecksumSequence(ref values);
					
					if (game_id_packet.data != game_id) {
						MainClass.HostDebugWriteLine("Game id does not match current game, discarding");
						hosting_state = HostGun.HostingState.HOSTING_STATE_ADDING;
						return false;
					}
					
					string debug = String.Format("Player {0:x} found, joining", new object[] { player_id });
					MainClass.HostDebugWriteLine(debug);
					
					foreach (UInt16[] packet in values) {
						TransmitBytes(packet[1],packet[0]);
					}
					
					adding_state.packets.Clear();
					
					hosting_state = HostGun.HostingState.HOSTING_STATE_CONFIRM_JOIN;
					confirm_join_state.confirm_timeout = now.AddSeconds(2);
					
				}
				return true;
			}
			case HostingState.HOSTING_STATE_CONFIRM_JOIN:
			{
				confirm_join_state.packets.AddLast(new IRPacket(type, data, number_of_bits));
				
				if (confirm_join_state.packets.Count == 4) {
					//TODO Check Checksum
					IRPacket game_id_packet = confirm_join_state.packets.First.Next.Value;
					IRPacket player_id_packet = confirm_join_state.packets.First.Next.Next.Value;
					UInt16 confirmed_game_id = game_id_packet.data;
					UInt16 confirmed_player_id = player_id_packet.data;
					
					if (game_id != confirmed_game_id
					    || confirm_join_state.player_id != confirmed_player_id)
					{
						string debug = String.Format("{0:x},{1:x},{2:x},{3:x}", 
						                             new object[] {
							game_id,
							confirmed_game_id,
							confirm_join_state.player_id,
							confirmed_player_id});
						MainClass.HostDebugWriteLine("Invalid confirmation: " + debug);
						hosting_state = HostGun.HostingState.HOSTING_STATE_ADDING;
						break;
					}
					
					MainClass.HostDebugWriteLine("Confirmed player");
					players.AddLast(new Player((byte)confirmed_game_id));
					adding_state.game_start_timeout = now.AddSeconds(WAIT_FOR_ADDITIONAL_PLAYERS_TIMEOUT_SECONDS);
					hosting_state = HostGun.HostingState.HOSTING_STATE_ADDING;
					confirm_join_state.packets.Clear();
				}
				
				return true;
			}
			default:
				break;
			}
			return false;
		}
		
		private bool ProcessMessage(string command, string[] parameters)
		{
			switch (command) {
			case "LTX":
			{
				if (parameters.Length != 2) {
					return false;
				}
				
				UInt16 data = UInt16.Parse(parameters[0], NumberStyles.AllowHexSpecifier);
				UInt16 number_of_bits = UInt16.Parse(parameters[1]);
				
				return ProcessPacket(IRPacket.PacketType.PACKET_TYPE_LTX, data, number_of_bits);
			}
			case "LTTO":
				break;
			default:
				break;
			}
			
			return false;
		}
		
		static public byte DecimalToDecimalHex(byte d)
		{
			byte result = (byte)(((d / 10) << 4) | (d % 10));
			return result;
		}
		
		public byte ComputeChecksum(ref UInt16[][] values)
		{
			int i = 0;
			byte sum = 0;
			for (i = 0; i < values.Length - 1; i++) {
				sum += (byte)values[i][1];
			}
			return sum;
		}
		
		public void ChecksumSequence(ref UInt16[][] values)
		{
			byte sum = ComputeChecksum(ref values);
			values[values.Length-1][1] |= sum;
		}
		
		public bool CheckChecksum(UInt16[][] values)
		{
			byte sum = ComputeChecksum(ref values);
			return ((values[values.Length-1][1] & 0xff) == sum);
		}
		
		public void Update() {
			DateTime now = DateTime.Now;
			
			if (serial_port.BytesToRead > 0) {
				string input = serial_port.ReadLine();
				MainClass.HostDebugWriteLine("RX: " + input);
				int command_length = input.IndexOf(':');
				if (command_length > 0) {
					string command = input.Substring(0,command_length);
					
					
					string paramters_line = input.Substring(command_length + 2);
					string[] paramters = paramters_line.Split(',');
					
					ProcessMessage(command, paramters);
					
				}
			}
			
			switch (hosting_state) {
			case HostingState.HOSTING_STATE_IDLE:
			{
				//TODO
				if (autostart) {
					hosting_state = HostingState.HOSTING_STATE_ADDING;
					adding_state.next_adv = DateTime.Now;
					adding_state.packets.Clear();
				}
				break;
			}
			case HostingState.HOSTING_STATE_ADDING:
			{
				if (now.CompareTo(adding_state.next_adv) > 0) {
					
					adding_state.packets.Clear();
					
					UInt16[][] values = new UInt16[][]{
						new UInt16[] {0x9,0x02},
						new UInt16[] {0x8,game_id},//Game ID
						new UInt16[] {0x8,0x10}, //game time minutes
						new UInt16[] {0x8,0x10}, //tags
						new UInt16[] {0x8,0xFF}, //reloads
						new UInt16[] {0x8,0x15}, //sheild
						new UInt16[] {0x8,0x10}, //mega
						new UInt16[] {0x8,0x20},
						new UInt16[] {0x8,0x01},
						new UInt16[] {0x9,0x100},
					};
					ChecksumSequence(ref values);
					foreach (UInt16[] packet in values) {
						TransmitBytes(packet[1],packet[0]);
					}
					
					adding_state.next_adv = now.AddSeconds(ADDING_ADVERTISEMENT_INTERVAL_SECONDS);
				} else if (players.Count >= 1
				           && now > adding_state.game_start_timeout)
				{
					MainClass.HostDebugWriteLine("Starting countdown");
					hosting_state = HostGun.HostingState.HOSTING_STATE_COUNTDOWN;
					countdown_state.game_start = now.AddSeconds(GAME_START_COUNTDOWN_INTERVAL_SECONDS);
					countdown_state.last_tick = now;
				} else if (players.Count >= 1) {
					//TODO Ratelimit
					//MainClass.HostDebugWriteLine("Game start in T-" + (adding_state.game_start_timeout - now).Seconds);
				}
				break;
			}
			case HostingState.HOSTING_STATE_CONFIRM_JOIN:
			{
				if (now.CompareTo(confirm_join_state.confirm_timeout) > 0) {
					MainClass.HostDebugWriteLine("No confirmation on timeout");
					hosting_state = HostGun.HostingState.HOSTING_STATE_ADDING;
					confirm_join_state.packets.Clear();
				}
				break;
			}
			case HostingState.HOSTING_STATE_COUNTDOWN:
			{
				if (countdown_state.game_start < now) {
					MainClass.HostDebugWriteLine("Starting Game");
					hosting_state = HostGun.HostingState.HOSTING_STATE_PLAYING;
					playing_state.game_end = now.AddMinutes(GAME_TIME_DURATION_MINUTES);
				} else if (countdown_state.last_tick.AddSeconds(1).Second < now.Second) {
					countdown_state.last_tick = now;
					
					int seconds_left = (countdown_state.game_start - now).Seconds;
					UInt16[][] values = new UInt16[][]{
						new UInt16[] {0x9,0x00},
						new UInt16[] {0x8,game_id},//Game ID
						new UInt16[] {0x8,DecimalToDecimalHex((byte)seconds_left)},
						new UInt16[] {0x8,0x02},
						new UInt16[] {0x8,0x00},
						new UInt16[] {0x8,0x00},
						new UInt16[] {0x9,0x100},
					};
					ChecksumSequence(ref values);
					foreach (UInt16[] packet in values) {
						TransmitBytes(packet[1],packet[0]);
					}
					MainClass.HostDebugWriteLine("T-" + seconds_left);
				}
				break;
			}
			case HostingState.HOSTING_STATE_PLAYING:
			{
				if (now > playing_state.game_end) {
					hosting_state = HostGun.HostingState.HOSTING_STATE_SUMMARY;
					MainClass.HostDebugWriteLine("Game Over");
				}
				break;
			}
			case HostingState.HOSTING_STATE_SUMMARY:
			{
				if (now > summary_state.last_announce.AddSeconds(5)) {
					summary_state.last_announce = now;
					UInt16[][] values = new UInt16[][]{
						new UInt16[] {0x9,0x31},
						new UInt16[] {0x8,game_id},//Game ID
						new UInt16[] {0x8,0x11},
						new UInt16[] {0x8,0x0F},
						new UInt16[] {0x9,0x100},
					};
					ChecksumSequence(ref values);
					foreach (UInt16[] packet in values) {
						TransmitBytes(packet[1],packet[0]);
					}
				}
				break;
			}
			default:
				break;
			}
		}
	}
	
	class MainClass
	{
		public static void HostDebugWriteLine(string line)
		{
			DateTime now = DateTime.Now;
			Console.WriteLine("Host: (" + now + ") " + line);
		}
		
		public static void Main (string[] args)
		{
			if (args.Length == 0) {
				Console.WriteLine("Format: LaserTagHost.exe <serial port>");
				return;
			}
			
			HostGun hg = new HostGun(args[0]);
			
			while (true) {
				
				hg.Update();
				
			}
		}
	}
}
