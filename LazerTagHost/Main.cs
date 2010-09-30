using System;
using System.IO.Ports;
using System.Globalization;
using System.Collections.Generic;
using System.Collections;

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
		public bool confirmed = false;
		public bool debreifed = false;
		//0 = solo
		//1-3 = team 1-3
		public int team_number;
		//0-7 = player 0-7
		public int player_number;
		
		public int damage = 0;
		public bool alive = false;
		
		public int score = 0;
		public int individual_rank = 0; //1-24
		public int team_rank = 0; //1-3
		
		public Player(byte player_id) {
			this.player_id = player_id;
		}
	}
	
	class HostGun
	{
		private struct GameState {
			public CommandCode game_type;
			public byte game_time_minutes;
			public byte tags;
			public byte reloads;
			public byte sheild;
			public byte mega;
			public bool team_tag;
			public bool medic_mode;
			public byte number_of_teams;
		};
		
		private struct AddingState {
			public DateTime next_adv;
			//public LinkedList<IRPacket> packets;
			public DateTime game_start_timeout;
		};
		
		private struct ConfirmJoinState {
			public byte player_id;
			public DateTime confirm_timeout;	
			//public LinkedList<IRPacket> packets;
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
		
		private struct GameOverState {
			public DateTime last_announce;
		};
		
		private enum HostingState {
			HOSTING_STATE_IDLE,
			HOSTING_STATE_ADDING,
			HOSTING_STATE_CONFIRM_JOIN,
			HOSTING_STATE_COUNTDOWN,
			HOSTING_STATE_PLAYING,
			HOSTING_STATE_SUMMARY,
			HOSTING_STATE_GAME_OVER,
		};
		
		private enum CommandCode {
			COMMAND_CODE_CUSTOM_GAME_MODE_HOST = 0x02,
			COMMAND_CODE_2TMS_GAME_MODE_HOST = 0x03,
			COMMAND_CODE_3TMS_GAME_MODE_HOST = 0x04,
			COMMAND_CODE_PLAYER_JOIN_GAME_REQUEST = 0x10,
			COMMAND_CODE_ACK_PLAYER_JOIN_RESPONSE = 0x01,
			COMMAND_CODE_CONFIRM_PLAY_JOIN_GAME = 0x11,
			COMMAND_CODE_COUNTDOWN_TO_GAME_START = 0x00,
			COMMAND_CODE_SCORE_ANNOUNCEMENT = 0x31,
			COMMAND_CODE_GAME_OVER = 0x32, //? unconfirmed
			COMMAND_CODE_PLAYER_REPORT_SCORE = 0x40,
			COMMAND_CODE_PLAYER_HIT_REPORT = 0x41,
			//COMMAND_CODE_RANK_REPORT = 0x42, //? unconfirmed
		};
		
		private byte game_id = 0x00;
		
		private SerialPort serial_port = null;
		private const int ADDING_ADVERTISEMENT_INTERVAL_SECONDS = 3;
		private const int WAIT_FOR_ADDITIONAL_PLAYERS_TIMEOUT_SECONDS = 60;
		private const int GAME_START_COUNTDOWN_INTERVAL_SECONDS = 30;
		private const int GAME_TIME_DURATION_MINUTES = 1;
		private const int MINIMUM_PLAYER_COUNT_START = 1;
		private const bool autostart = true;
		
		//host gun state
		private GameState game_state;
		private HostingState hosting_state = HostGun.HostingState.HOSTING_STATE_IDLE;
		private AddingState adding_state;
		private ConfirmJoinState confirm_join_state;
		private CountdownState countdown_state;
		private SummaryState summary_state;
		private PlayingState playing_state;
		private GameOverState game_over_state;
		private LinkedList<Player> players = new LinkedList<Player>();
		
		private List<IRPacket> incoming_packet_queue = new List<IRPacket>();
		
		private void BaseGameSet(byte game_time_minutes, 
		                              byte tags,
		                              byte reloads,
		                              byte sheilds,
		                              byte mega,
		                              bool team_tag,
		                              bool medic_mode)
		{
			game_state.game_time_minutes = game_time_minutes;
			game_state.tags = tags;
			game_state.reloads = reloads;
			game_state.sheild = sheilds;
			game_state.mega = mega;
			game_state.team_tag = team_tag;
			game_state.medic_mode = medic_mode;
			game_id = (byte)(new Random().Next());
		}
		
		public void Init2TeamHostMode(byte game_time_minutes, 
		                              byte tags,
		                              byte reloads,
		                              byte sheilds,
		                              byte mega,
		                              bool team_tag,
		                              bool medic_mode)
		{
			game_state.game_type = CommandCode.COMMAND_CODE_2TMS_GAME_MODE_HOST;
			BaseGameSet(game_time_minutes,
			            tags,
                        reloads,
                        sheilds,
                        mega,
                        team_tag,
                        medic_mode);
			game_state.number_of_teams = 2;
		}
		
		public void Init3TeamHostMode(byte game_time_minutes, 
		                              byte tags,
		                              byte reloads,
		                              byte sheilds,
		                              byte mega,
		                              bool team_tag,
		                              bool medic_mode)
		{
			game_state.game_type = CommandCode.COMMAND_CODE_3TMS_GAME_MODE_HOST;
			BaseGameSet(game_time_minutes,
			            tags,
                        reloads,
                        sheilds,
                        mega,
                        team_tag,
                        medic_mode);
			game_state.number_of_teams = 3;
		}
		
		public void InitCustomHostMode(byte game_time_minutes, 
		                              byte tags,
		                              byte reloads,
		                              byte sheilds,
		                              byte mega,
		                              bool team_tag,
		                              bool medic_mode)
		{
			game_state.game_type = CommandCode.COMMAND_CODE_CUSTOM_GAME_MODE_HOST;
			BaseGameSet(game_time_minutes,
			            tags,
                        reloads,
                        sheilds,
                        mega,
                        team_tag,
                        medic_mode);
			game_state.number_of_teams = 1;
		}
		
		private void TransmitPacket(ref UInt16[][] values)
		{
			string debug = "TX: (";
			
			debug += ((CommandCode)(values[0][1])).ToString() + ") ";
			
			foreach (UInt16[] packet in values) {
				TransmitBytes(packet[1],packet[0]);
				debug += String.Format("{0:x},", packet[1]);
			}
			MainClass.HostDebugWriteLine(debug);
		}
		
		private void TransmitBytes(UInt16 data, UInt16 number_of_bits)
		{
			byte[] packet = new byte[2] {
				(byte)((number_of_bits << 4) | ((data >> 8) & 0xf)),
				(byte)(data & 0xff),
			};
			serial_port.Write( packet, 0, 2 );
			serial_port.BaseStream.Flush();
			//string debug = String.Format("TX: {0:x}, {1:d}", new object[] {data, number_of_bits});
			//MainClass.HostDebugWriteLine(debug);
			System.Threading.Thread.Sleep(100);
		}
		
		public HostGun(string device) {
			serial_port = new SerialPort(device, 115200);
			serial_port.Open();
		}
		
		private static string GetCommandCodeName(CommandCode code)
		{
			Enum c = code;
			return c.ToString();
		}
		
		private bool AssignTeamAndPlayer(int team_request, 
		                                 ref int team_assignment, 
		                                 ref int player_assignment)
		{
			switch (game_state.game_type) {
			case CommandCode.COMMAND_CODE_CUSTOM_GAME_MODE_HOST:
				team_assignment = 0;
				break;
			case CommandCode.COMMAND_CODE_2TMS_GAME_MODE_HOST:
			case CommandCode.COMMAND_CODE_3TMS_GAME_MODE_HOST:
				if (team_request > 0 && team_request <= game_state.number_of_teams) {
					team_assignment = team_request;
				} else {
					//team request is any or invalid
					
					int i = 0;
					int lowest_team_player_count = 0;
					int max_player_count = 8;
					for (i = 1; i <= game_state.number_of_teams; i++) {
						int player_count = 0;
						foreach (Player p in players) {
							if (p.team_number == i) player_count++;
						}
						if (player_count < max_player_count) {
							lowest_team_player_count = i;
							max_player_count = player_count;
						}
					}
					if (lowest_team_player_count != 0) {
						team_assignment = lowest_team_player_count;
					} else {
						MainClass.HostDebugWriteLine("Unable to assign team");
						return false;
					}
				}
				break;
			default:
				MainClass.HostDebugWriteLine("Unable to assign team");
				return false;
			}
			
			switch (game_state.game_type) {
			case CommandCode.COMMAND_CODE_CUSTOM_GAME_MODE_HOST:
			case CommandCode.COMMAND_CODE_2TMS_GAME_MODE_HOST:
			case CommandCode.COMMAND_CODE_3TMS_GAME_MODE_HOST:
				int i = 0;
				for (i = 0; i < 8; i++) {
					bool found = false;
					foreach (Player p in players) {
						if (team_assignment != p.team_number) continue;
						if (i == p.player_number) {
							found = true;
							break;
						}
					}
					if (!found) {
						player_assignment = i;
						break;
					}
				}
				if (i == 8) {
					MainClass.HostDebugWriteLine("Failed to assign player number");
					return false;
				}
				break;
			default:
				return false;
			}
			
			MainClass.HostDebugWriteLine("Assigned player to team " + team_assignment + " and player " + player_assignment);
			
			return true;
		}
		
		private bool ProcessCommandSequence()
		{
			DateTime now = DateTime.Now;
			
			switch (hosting_state) {
			case HostingState.HOSTING_STATE_IDLE:
				return true;
			case HostingState.HOSTING_STATE_ADDING:
			{
				
				if (incoming_packet_queue.Count != 4) {
					return false;
				}
					
				IRPacket command_packet = incoming_packet_queue[0];
				IRPacket game_id_packet = incoming_packet_queue[1];
				IRPacket player_id_packet = incoming_packet_queue[2];
				IRPacket player_team_request_packet = incoming_packet_queue[3];
				UInt16 player_id = player_id_packet.data;
				
				if ((CommandCode)command_packet.data != CommandCode.COMMAND_CODE_PLAYER_JOIN_GAME_REQUEST) {
					MainClass.HostDebugWriteLine("Wrong command");
					return false;
				}
				
				if (game_id != game_id_packet.data) {
					MainClass.HostDebugWriteLine("Wrong game id");
					return false;
				}
				
				bool collision = false;
				foreach (Player collision_check_player in players) {
					if (collision_check_player.player_id == player_id) {
						collision = true;
						break;
					}
				}
				if (collision) {
					MainClass.HostDebugWriteLine("Player id collision");
					return false;
				}
				
				confirm_join_state.player_id = (byte)player_id;
				
				/* 
				 * 0 = any
				 * 1-3 = team 1-3
				 */
				UInt16 team_request = (UInt16)(player_team_request_packet.data & 0x03);
				
				Player p = new Player((byte)player_id);
				
				//TODO: Pick team/player index
				//0 = solo
				//1-3 = team 1-3
				int team_assignment = 0;
				//player 0-7
				int player_assignment = 0;
				
				if (!AssignTeamAndPlayer(team_request,
				                    ref team_assignment, 
				                    ref player_assignment))
				{
					return false;
				}
				p.team_number = team_assignment;
				p.player_number = player_assignment;
				
				players.AddLast(p);
				
				UInt16 team_response = (UInt16)((team_assignment << 3) | (player_assignment));
				
				
				UInt16[][] values = new UInt16[][]{
					new UInt16[] {0x9,0x01},
					new UInt16[] {0x8,game_id},//Game ID
					new UInt16[] {0x8,player_id},//Player ID
					new UInt16[] {0x8,team_response}, //player #
					// [3 bits - zero - unknown][2 bits - team assignment][3 bits - player assignment]
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
				
				TransmitPacket(ref values);
				
				incoming_packet_queue.Clear();
				
				hosting_state = HostGun.HostingState.HOSTING_STATE_CONFIRM_JOIN;
				confirm_join_state.confirm_timeout = now.AddSeconds(2);
				
				return true;
			}
			case HostingState.HOSTING_STATE_CONFIRM_JOIN:
			{
				
				if (incoming_packet_queue.Count != 3) {
					return false;
				}
					
				IRPacket command_packet = incoming_packet_queue[0];
				IRPacket game_id_packet = incoming_packet_queue[1];
				IRPacket player_id_packet = incoming_packet_queue[2];
				UInt16 confirmed_game_id = game_id_packet.data;
				UInt16 confirmed_player_id = player_id_packet.data;
				
				if ((CommandCode)command_packet.data != CommandCode.COMMAND_CODE_CONFIRM_PLAY_JOIN_GAME) {
					MainClass.HostDebugWriteLine("Wrong command");
					return false;
				}
				
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
				
				bool found = false;
				foreach(Player p in players) {
					if (p.player_id == confirmed_player_id) {
						p.confirmed = true;
						found = true;
						break;
					}
				}
				if (found) {
					MainClass.HostDebugWriteLine("Confirmed player");
				} else {
					MainClass.HostDebugWriteLine("Unable to find player to confirm");
					return false;
				}
				
				if (players.Count >= MINIMUM_PLAYER_COUNT_START) {
					adding_state.game_start_timeout = now.AddSeconds(WAIT_FOR_ADDITIONAL_PLAYERS_TIMEOUT_SECONDS);
				}
				hosting_state = HostGun.HostingState.HOSTING_STATE_ADDING;
				incoming_packet_queue.Clear();
				
				return true;
			}
			case HostingState.HOSTING_STATE_SUMMARY:
			{
				if (incoming_packet_queue.Count != 9) {
					return false;
				}
				
				IRPacket command_packet = incoming_packet_queue[0];
				IRPacket game_id_packet = incoming_packet_queue[1];
				IRPacket player_index_packet = incoming_packet_queue[2];
				
				IRPacket damage_recv_packet = incoming_packet_queue[3]; //decimal hex
				IRPacket still_alive_packet = incoming_packet_queue[4]; //[7 bits - zero - unknown][1 bit - alive]
				
				UInt16 confirmed_game_id = game_id_packet.data;
				UInt16 player_index = player_index_packet.data;
				
				if ((CommandCode)command_packet.data != CommandCode.COMMAND_CODE_PLAYER_REPORT_SCORE) {
					MainClass.HostDebugWriteLine("Wrong command");
					return false;
				}
				
				if (game_id != confirmed_game_id) {
					MainClass.HostDebugWriteLine("Wrong game id");
					return false;
				}
				
				int team_number = (player_index >> 4) & 0xf;
				int player_number = player_index & 0xf;
				bool found = false;
				
				foreach (Player p in players) {
					if (p.team_number == team_number
					    && p.player_number == player_number)
					{
						found = true;
						p.debreifed = true;
						
						p.alive = (still_alive_packet.data == 0x01);
						p.damage = DecimalHexToDecimal(damage_recv_packet.data);
						
						//TODO record score
						break;
					}
				}
				
				if (!found) {
					MainClass.HostDebugWriteLine("Unable to find player for score report");
					return false;
				}
				
				
				
				/*UInt16[][] values = new UInt16[][]{
					new UInt16[] {0x9,(UInt16)CommandCode.COMMAND_CODE_PLAYER_REPORT_SCORE_ACK},
					new UInt16[] {0x8,game_id},
					new UInt16[] {0x8,player_index},
					new UInt16[] {0x8,2}, //unknown
					new UInt16[] {0x8,3}, //unknown
					new UInt16[] {0x9,0x100},
				};
				ChecksumSequence(ref values);
				
				TransmitPacket(ref values);*/
				
				string debug = String.Format("Debreifed team {0:d} player {1:d}", team_number, player_number);
				MainClass.HostDebugWriteLine(debug);
				
				return true;
			}
			default:
				break;
			}
			return false;
		}
		
		private string SerializeCommandSequence(ref List<IRPacket> packets)
		{
			string command = "SEQ:";
			foreach (IRPacket packet in packets) {
				command += String.Format("{0:x},", packet.data);
			}
			return command;
		}
		
		private bool ProcessPacket(IRPacket.PacketType type, UInt16 data, UInt16 number_of_bits)
		{
			//DateTime now = DateTime.Now;
			
			if (type != IRPacket.PacketType.PACKET_TYPE_LTX) return false;
			
			if (number_of_bits == 9)
			{
				if ((data & 0x100) != 0) {
					//end sequence
					if ((data & 0xff) == ComputeChecksum(ref incoming_packet_queue)) {
						MainClass.HostDebugWriteLine("Command: (" 
						                             + GetCommandCodeName((CommandCode)(incoming_packet_queue[0].data)) 
						                             + ") " 
						                             + SerializeCommandSequence(ref incoming_packet_queue));
						if (!ProcessCommandSequence()) {
							MainClass.HostDebugWriteLine("ProcessCommandSequence failed: " + SerializeCommandSequence(ref incoming_packet_queue));
						}
					} else {
						MainClass.HostDebugWriteLine("Invalid Checksum SEQ: " + SerializeCommandSequence(ref incoming_packet_queue));
					}
					incoming_packet_queue.Clear();
				} else {
					//start sequence
					incoming_packet_queue.Add(new IRPacket(type, data, number_of_bits));
				}
			} else if (number_of_bits == 8
			           && incoming_packet_queue.Count > 0)
			{
				//mid sequence
				incoming_packet_queue.Add(new IRPacket(type, data, number_of_bits));
			} else if (number_of_bits == 8) {
				//junk
				MainClass.HostDebugWriteLine("Unknown packet, clearing queue");
				incoming_packet_queue.Clear();
			} else {
				string debug = String.Format(type.ToString() + " {0:x}, {1:d}",data, number_of_bits);
				MainClass.HostDebugWriteLine(debug);
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
			//Unlimited
			if (d == 0xff) return d;
			//Otherwise
			byte result = (byte)(((d / 10) << 4) | (d % 10));
			return result;
		}
		
		static public int DecimalHexToDecimal(int d)
		{
			int ret = d & 0x0f;
			ret += ((d >> 4) & 0xf);
			return ret;
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
		
		public byte ComputeChecksum(ref List<IRPacket> values)
		{
			byte sum = 0;
			foreach (IRPacket packet in values) {
				//don't add the checksum value in
				if ((packet.data & 0x100) == 0) {
					sum += (byte)packet.data;
				}
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
		
		private void RankPlayers()
		{
			//TODO
			
			switch (game_state.game_type) {
			case CommandCode.COMMAND_CODE_2TMS_GAME_MODE_HOST:
			case CommandCode.COMMAND_CODE_3TMS_GAME_MODE_HOST:
			{
				SortedList<int, Player> rankings = new SortedList<int, Player>();
				//for team score
				int[] players_alive_per_team = new int[4] { 0,0,0,0 };
				//for tie breaking team scores
				int[] team_alive_score = new int[4] {0,0,0,0};
				int[] team_rank = new int[4] {0,0,0,0};
				SortedList<int, int> team_rankings = new SortedList<int, int>();
				/*
				- Individual ranks are based on receiving 2 points per tag landed on players
				from other teams, and losing 1 point for every time you’re tagged by a player
				from another team. Tagging your own teammate (Team Tags) costs you 2
				points. Being tagged by your own teammates does not hurt your score.
				- Team ranks are based on which team has the most players not tagged out
				when the game ends (this gives an advantage to larger teams, so less-skilled
				players can be grouped together on a larger team to even things out). In the
				event of a tie, the TAG MASTER will attempt to break the tie based on the
				individual scores of the players on each team who did not get tagged out –
				this rewards the team with the more aggressive players that land more tags.
				Just hiding or trying to not get tagged may cost your team valuable points that
				could affect your team’s ranking.
				*/
				
				foreach (Player p in players) {
					//TODO: add hits and team hits
					int score = - p.damage;
					p.score = score;
					if (p.alive) {
						players_alive_per_team[p.team_number] ++;
						team_alive_score[p.team_number] += score;
					}
					rankings.Add(score, p);
				}
				
				//Determine Team Ranks
				/*team_rankings.Add((players_alive_per_team[1] << 8) + team_alive_score[1], 1);
				team_rankings.Add((players_alive_per_team[2] << 8) + team_alive_score[2], 2);
				team_rankings.Add((players_alive_per_team[3] << 8) + team_alive_score[3], 3);
				//Teams are sorted by score
				team_rank[team_rankings.Values[0]] = 1;
				team_rank[team_rankings.Values[1]] = 2;
				team_rank[team_rankings.Values[2]] = 3;
				
				//Determine PlayerRanks
				int rank = 0;
				int last_score = 99;
				foreach(KeyValuePair<int, Player> e in rankings) {
					
					Player p = e.Value;
					if (p.score != last_score) {
						rank++;
						last_score = p.score;
					}
					p.individual_rank = rank;
					p.team_rank = team_rank[p.team_number];
				}*/
				
				break;
			}
			default:
				break;
			}

		}
		
		public void Update() {
			DateTime now = DateTime.Now;
			
			if (serial_port.BytesToRead > 0) {
				string input = serial_port.ReadLine();
				
				int command_length = input.IndexOf(':');
				if (command_length > 0) {
					string command = input.Substring(0,command_length);
					
					
					string paramters_line = input.Substring(command_length + 2);
					string[] paramters = paramters_line.Split(',');
					
					ProcessMessage(command, paramters);
					
				} else {
					MainClass.HostDebugWriteLine("DEBUG: " + input);
				}
			}
			
			switch (hosting_state) {
			case HostingState.HOSTING_STATE_IDLE:
			{
				//TODO
				if (autostart) {
					hosting_state = HostingState.HOSTING_STATE_ADDING;
					adding_state.next_adv = DateTime.Now;
					incoming_packet_queue.Clear();
					Init2TeamHostMode(GAME_TIME_DURATION_MINUTES,10,0xff,15,10,true,false);
				}
				break;
			}
			case HostingState.HOSTING_STATE_ADDING:
			{
				if (now.CompareTo(adding_state.next_adv) > 0) {
					
					incoming_packet_queue.Clear();
					
					byte flags = 0x20;
						flags |= game_state.medic_mode ? (byte)0x08 : (byte)0x00;
						flags |= game_state.team_tag ? (byte)0x10 : (byte)0x00;
					
					UInt16[][] values = new UInt16[][]{
						new UInt16[] {0x9,(UInt16)game_state.game_type},
						new UInt16[] {0x8,game_id},//Game ID
						new UInt16[] {0x8,DecimalToDecimalHex(game_state.game_time_minutes)}, //game time minutes
						new UInt16[] {0x8,DecimalToDecimalHex(game_state.tags)}, //tags
						new UInt16[] {0x8,DecimalToDecimalHex(game_state.reloads)}, //reloads
						new UInt16[] {0x8,DecimalToDecimalHex(game_state.sheild)}, //sheild
						new UInt16[] {0x8,DecimalToDecimalHex(game_state.mega)}, //mega
						new UInt16[] {0x8,flags}, //unknown/team/medic/unknown
						//[3 bits - b001 - unknown][1 bit - team tag][1 bit - medic mode][3 bits - 0x0 - unknown]
						new UInt16[] {0x8,DecimalToDecimalHex(game_state.number_of_teams)}, //number of teams
						new UInt16[] {0x9,0x100},
					};
					ChecksumSequence(ref values);
					
					TransmitPacket(ref values);
					
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
					incoming_packet_queue.Clear();
				}
				break;
			}
			case HostingState.HOSTING_STATE_COUNTDOWN:
			{
				if (countdown_state.game_start < now) {
					MainClass.HostDebugWriteLine("Starting Game");
					hosting_state = HostGun.HostingState.HOSTING_STATE_PLAYING;
					playing_state.game_end = now.AddMinutes(game_state.game_time_minutes);
				} else if (countdown_state.last_tick.AddSeconds(1) < now) {
					countdown_state.last_tick = now;
					
					int seconds_left = (countdown_state.game_start - now).Seconds;
					UInt16[][] values = new UInt16[][]{
						new UInt16[] {0x9,0x00},
						new UInt16[] {0x8,game_id},//Game ID
						new UInt16[] {0x8,DecimalToDecimalHex((byte)seconds_left)},
						new UInt16[] {0x8,0x02}, //unknown
						new UInt16[] {0x8,0x00},
						new UInt16[] {0x8,0x00},
						new UInt16[] {0x9,0x100},
					};
					ChecksumSequence(ref values);
					TransmitPacket(ref values);
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
					
					Player next_debreif = null;
					
					foreach (Player p in players) {
						if (!p.debreifed) {
							next_debreif = p;
							break;
						}
					}
					if (next_debreif == null) {
						//TODO Next State
						MainClass.HostDebugWriteLine("All players breifed");
						hosting_state = HostGun.HostingState.HOSTING_STATE_GAME_OVER;
						game_over_state.last_announce = now;
						RankPlayers();
						break;
					}
					
					UInt16 player_index = (UInt16)((next_debreif.team_number & 0xf) << 4 | (next_debreif.player_number & 0xf));
					
					summary_state.last_announce = now;
					UInt16[][] values = new UInt16[][]{
						new UInt16[] {0x9,0x31},
						new UInt16[] {0x8,game_id},//Game ID
						new UInt16[] {0x8,player_index},//player index
						// [ 4 bits - team ] [ 4 bits - player number ]
						new UInt16[] {0x8,0x0F}, //unknown
						new UInt16[] {0x9,0x100},
					};
					ChecksumSequence(ref values);
					TransmitPacket(ref values);
				}
				break;
			}
			case HostingState.HOSTING_STATE_GAME_OVER:
			{
				if (now > game_over_state.last_announce.AddSeconds(5)) {
					
					game_over_state.last_announce = now;
					
					foreach (Player p in players) {
						
						UInt16 player_index = (UInt16)((p.team_number & 0xf) << 4 | (p.player_number & 0xf));
						
						UInt16[][] values = new UInt16[][]{
							new UInt16[] {0x9,(UInt16)CommandCode.COMMAND_CODE_GAME_OVER},
							new UInt16[] {0x8,game_id},//Game ID
							new UInt16[] {0x8,player_index},//player index
							// [ 4 bits - team ] [ 4 bits - player number ]
							new UInt16[] {0x8,0x00}, //player rank (not decimal hex, 1-player_count)
							new UInt16[] {0x8,0x01}, //unknown
							new UInt16[] {0x8,0x02},
							new UInt16[] {0x8,0x03},
							new UInt16[] {0x8,0x00},
							new UInt16[] {0x8,0x00},
							new UInt16[] {0x8,0x00},
							new UInt16[] {0x9,0x100},
						};
						ChecksumSequence(ref values);
						TransmitPacket(ref values);
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
