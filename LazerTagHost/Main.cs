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
        public bool debriefed = false;
        //0 = solo
        //1-3 = team 1-3
        public int team_number;
        //0-7 = player 0-7
        public int player_number;
        //damage taken during match
        public int damage = 0;
        //still alive at end of match
        public bool alive = false;
        
        public bool[] has_score_report_for_team = new bool[3] {false, false, false};
        public int[,] hit_by_team_player_count = new int[3,8];
        public int[,] hit_team_player_count = new int[3,8];
        
        //final score for given game mode
        public int score = 0;
        public int individual_rank = 0; //1-24
        public int team_rank = 0; //1-3
        
        public Player(byte player_id) {
            this.player_id = player_id;
        }
        
        public bool HasBeenDebriefed() {
            return debriefed
                && !has_score_report_for_team[0]
                && !has_score_report_for_team[1]
                && !has_score_report_for_team[2];
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
            COMMAND_CODE_PLAYER_HIT_BY_TEAM_1_REPORT = 0x41,
            COMMAND_CODE_PLAYER_HIT_BY_TEAM_2_REPORT = 0x42,
            COMMAND_CODE_PLAYER_HIT_BY_TEAM_3_REPORT = 0x43,
        };
        
        private byte game_id = 0x00;
        
        private SerialPort serial_port = null;
        private const int ADDING_ADVERTISEMENT_INTERVAL_SECONDS = 3;
        private const int WAIT_FOR_ADDITIONAL_PLAYERS_TIMEOUT_SECONDS = 10;
        private const int GAME_START_COUNTDOWN_INTERVAL_SECONDS = 20;
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

        private void TransmitPacket2(ref UInt16[] values)
        {
            string debug = "TX: (";

            debug += ((CommandCode)(values[0])).ToString() + ") ";

            for (int i = 0; i < values.Length; i++) {
                UInt16 packet = values[i];
                TransmitBytes(packet,(UInt16)(i == 0 ? 9 : 8));
                debug += String.Format("{0:x},", packet);
            }
            UInt16 checksum = ComputeChecksum2(ref values);
            checksum |= 0x100;
            TransmitBytes(checksum,9);

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
            {
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
            }
            default:
                return false;
            }
            
            MainClass.HostDebugWriteLine("Assigned player to team " + team_assignment + " and player " + player_assignment);
            
            return true;
        }
        
        private bool ProcessPlayerReportScore()
        {
            if (incoming_packet_queue.Count != 9) {
                return false;
            }
            
            IRPacket command_packet = incoming_packet_queue[0];
            IRPacket game_id_packet = incoming_packet_queue[1];
            IRPacket player_index_packet = incoming_packet_queue[2];
            
            IRPacket damage_recv_packet = incoming_packet_queue[3]; //decimal hex
            IRPacket still_alive_packet = incoming_packet_queue[4]; //[7 bits - zero - unknown][1 bit - alive]
            
            //[4 bits - zero - unknown][1 bit - hit by t3][1 bit - hit by t2][1 bit - hit by t1][1 bit - zero - unknown]
            IRPacket team_hit_report = incoming_packet_queue[8]; 
            
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
                    p.debriefed = true;
                    
                    p.alive = (still_alive_packet.data == 0x01);
                    p.damage = DecimalHexToDecimal(damage_recv_packet.data);
                    p.has_score_report_for_team[0] = (team_hit_report.data & 0x2) != 0;
                    p.has_score_report_for_team[1] = (team_hit_report.data & 0x4) != 0;
                    p.has_score_report_for_team[2] = (team_hit_report.data & 0x8) != 0;
                    
                    break;
                }
            }
            
            if (!found) {
                MainClass.HostDebugWriteLine("Unable to find player for score report");
                return false;
            }
            
            string debug = String.Format("Debriefed team {0:d} player {1:d}", team_number, player_number);
            MainClass.HostDebugWriteLine(debug);
            
            return true;
        }
        
        private Player LookupPlayer(int team_number, int player_number)
        {
            foreach (Player p in players) {
                if (p.team_number == team_number
                    && p.player_number == player_number)
                {
                    return p;
                }
            }
            MainClass.HostDebugWriteLine("Unable to lookup player " + team_number + "," + player_number);
            return null;
        }
        
        private void PlayerTeamNumberSplit(int team_player_id, ref int player_number, ref int team_number)
        {
            player_number = team_player_id & 0xf;
            team_number = (team_player_id >> 4) & 0xf;
        }

        private bool ProcessPlayerHitByTeamReport()
        {
            if (incoming_packet_queue.Count <= 4) {
                return false;
            }
            
            IRPacket command_packet = incoming_packet_queue[0];
            IRPacket game_id_packet = incoming_packet_queue[1];
            IRPacket player_id_packet = incoming_packet_queue[2];
            IRPacket score_bitmask_packet = incoming_packet_queue[3];
            
            //what team do the scores relate to hits from
            int team_index = ((int)command_packet.data - (int)CommandCode.COMMAND_CODE_PLAYER_HIT_BY_TEAM_1_REPORT);
            int player_number = 0;
            int team_number = 0;
            PlayerTeamNumberSplit(player_id_packet.data, ref player_number, ref team_number);

            if (game_id != game_id_packet.data) {
                MainClass.HostDebugWriteLine("Wrong game id");
                return false;
            }

            Player p = LookupPlayer(team_number, player_number);
            if (p == null) {
                return false;
            }
            
            if (!p.has_score_report_for_team[team_index]) {
                MainClass.HostDebugWriteLine("Score report already reported");
                return false;
            }
            p.has_score_report_for_team[team_index] = false;
            
            int i = 0;
            int score_index = 4;
            byte mask = (byte)score_bitmask_packet.data;
            for (i = 0; i < 8; i++) {
                bool score_present = ((mask >> i) & 0x1) != 0;
                if (!score_present) continue;
                
                if (incoming_packet_queue.Count <= score_index) {
                    MainClass.HostDebugWriteLine("Ran off end of score report");
                    return false;
                }
                
                IRPacket score_packet = incoming_packet_queue[score_index];
                
                p.hit_by_team_player_count[team_index,i] = score_packet.data;
                Player shooter = LookupPlayer(team_index + 1,i);
                if (shooter == null) {
                    continue;
                }
                string debug = String.Format("Hit: {0:d},{1:d}", p.team_number - 1, p.player_number);
                MainClass.HostDebugWriteLine(debug);
                shooter.hit_team_player_count[p.team_number - 1, p.player_number] = score_packet.data;
                
                score_index++;
            }
            
            return true;
        }
        
        private void PrintScoreReport()
        {
            foreach (Player p in players)
            {
                string debug = String.Format("Player 0x{0:x} {1:d},{2:d}",
                                             p.player_id, p.team_number, p.player_number);
                MainClass.HostDebugWriteLine(debug);
                debug = String.Format("\tRank: {0:d} Team Rank: {1:d} Score: {2:d}", p.individual_rank, p.team_rank, p.score);
                MainClass.HostDebugWriteLine(debug);
                int i = 0;
                for (i = 0; i < 3; i++) {
                    bool hits_by_team = (p.hit_by_team_player_count[i,0] +
                                          p.hit_by_team_player_count[i,1] +
                                          p.hit_by_team_player_count[i,2] +
                                          p.hit_by_team_player_count[i,3] +
                                          p.hit_by_team_player_count[i,4] +
                                          p.hit_by_team_player_count[i,5] +
                                          p.hit_by_team_player_count[i,6] +
                                          p.hit_by_team_player_count[i,7]) > 0;
                    if (hits_by_team) {
                        debug = String.Format("\tHits By Team {8:d} Player: {0:d},{1:d},{2:d},{3:d},{4:d},{5:d},{6:d},{7:d}",
                                              p.hit_by_team_player_count[i,0],
                                              p.hit_by_team_player_count[i,1],
                                              p.hit_by_team_player_count[i,2],
                                              p.hit_by_team_player_count[i,3],
                                              p.hit_by_team_player_count[i,4],
                                              p.hit_by_team_player_count[i,5],
                                              p.hit_by_team_player_count[i,6],
                                              p.hit_by_team_player_count[i,7],
                                              i + 1);
                        MainClass.HostDebugWriteLine(debug);
                    }
                    bool hits_to_team = (p.hit_by_team_player_count[i,0] +
                                          p.hit_team_player_count[i,1] +
                                          p.hit_team_player_count[i,2] +
                                          p.hit_team_player_count[i,3] +
                                          p.hit_team_player_count[i,4] +
                                          p.hit_team_player_count[i,5] +
                                          p.hit_team_player_count[i,6] +
                                          p.hit_team_player_count[i,7]) > 0;
                    if (hits_to_team) {
                        debug = String.Format("\tHits To Team {8:d} Player: {0:d},{1:d},{2:d},{3:d},{4:d},{5:d},{6:d},{7:d}",
                                              p.hit_team_player_count[i,0],
                                              p.hit_team_player_count[i,1],
                                              p.hit_team_player_count[i,2],
                                              p.hit_team_player_count[i,3],
                                              p.hit_team_player_count[i,4],
                                              p.hit_team_player_count[i,5],
                                              p.hit_team_player_count[i,6],
                                              p.hit_team_player_count[i,7],
                                              i + 1);
                        MainClass.HostDebugWriteLine(debug);
                    }
                }
            }
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
                
                
                UInt16[] values = new UInt16[]{
                    (UInt16)CommandCode.COMMAND_CODE_ACK_PLAYER_JOIN_RESPONSE,
                    game_id,//Game ID
                    player_id,//Player ID
                    team_response, //player #
                    // [3 bits - zero - unknown][2 bits - team assignment][3 bits - player assignment]
                };
                
                if (game_id_packet.data != game_id) {
                    MainClass.HostDebugWriteLine("Game id does not match current game, discarding");
                    hosting_state = HostGun.HostingState.HOSTING_STATE_ADDING;
                    return false;
                }
                
                string debug = String.Format("Player {0:x} found, joining", new object[] { player_id });
                MainClass.HostDebugWriteLine(debug);
                
                TransmitPacket2(ref values);
                
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
                IRPacket command_packet = incoming_packet_queue[0];
                switch ((CommandCode)command_packet.data) {
                case CommandCode.COMMAND_CODE_PLAYER_REPORT_SCORE:
                    return ProcessPlayerReportScore();
                case CommandCode.COMMAND_CODE_PLAYER_HIT_BY_TEAM_1_REPORT:
                case CommandCode.COMMAND_CODE_PLAYER_HIT_BY_TEAM_2_REPORT:
                case CommandCode.COMMAND_CODE_PLAYER_HIT_BY_TEAM_3_REPORT:
                    return ProcessPlayerHitByTeamReport();
                default:
                    break;
                }
                
                return false;
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

        [Obsolete("[][]")]
        public byte ComputeChecksum(ref UInt16[][] values)
        {
            int i = 0;
            byte sum = 0;
            for (i = 0; i < values.Length - 1; i++) {
                sum += (byte)values[i][1];
            }
            return sum;
        }

        public byte ComputeChecksum2(ref UInt16[]values)
        {
            int i = 0;
            byte sum = 0;
            for (i = 0; i < values.Length - 1; i++) {
                sum += (byte)values[i];
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

        [Obsolete("[][]")]
        public void ChecksumSequence(ref UInt16[][] values)
        {
            byte sum = ComputeChecksum(ref values);
            values[values.Length-1][1] |= sum;
        }

        public void ChecksumSequence2(ref UInt16[] values)
        {
            byte sum = ComputeChecksum2(ref values);
            values[values.Length-1] |= sum;
        }

        [Obsolete("[][]")]
        public bool CheckChecksum(UInt16[][] values)
        {
            byte sum = ComputeChecksum(ref values);
            return ((values[values.Length-1][1] & 0xff) == sum);
        }
        
        public void RunRankTests()
        {
            game_state.game_type = CommandCode.COMMAND_CODE_3TMS_GAME_MODE_HOST;
            hosting_state = HostingState.HOSTING_STATE_GAME_OVER;
            
            Player p1 = new Player(0x01);
            Player p2 = new Player(0x02);
            Player p3 = new Player(0x03);
            players.AddLast(p1);
            players.AddLast(p2);
            players.AddLast(p3);
            
            p1.team_number = 1;
            p1.player_number = 0;
            p1.alive = true;
            p2.team_number = 2;
            p2.player_number = 1;
            p2.alive = true;
            p3.team_number = 3;
            p3.player_number = 3;
            p3.alive = false;

            p1.hit_by_team_player_count[1,1] = 6;
            p2.hit_team_player_count[2,0] = 6;

            p1.hit_team_player_count[0,5] = 6;
            
            RankPlayers();
            PrintScoreReport();
        }
        
        private void RankPlayers()
        {
            switch (game_state.game_type) {
            case CommandCode.COMMAND_CODE_2TMS_GAME_MODE_HOST:
            case CommandCode.COMMAND_CODE_3TMS_GAME_MODE_HOST:
            {
                SortedList<int, Player> rankings = new SortedList<int, Player>();
                //for team score
                int[] players_alive_per_team = new int[3] { 0,0,0, };
                //for tie breaking team scores
                int[] team_alive_score = new int[3] { 0,0,0, };
                int[] team_final_score = new int[3] { 0,0,0, };
                //rank for each team
                int[] team_rank = new int[3] { 0,0,0, };
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
                    int score = - p.damage;
                    int team_index = 0;
                    int player_index = 0;
                    //TODO: test
                    for (team_index = 0; team_index < 3; team_index++) {
                        for (player_index = 0; player_index < 8; player_index++) {
                            if (p.team_number - 1 == team_index) {
                                //Friendly fire
                                score -= 2 * p.hit_team_player_count[team_index, player_index];
                            } else {
                                score += 2 * p.hit_team_player_count[team_index, player_index];
                            }
                        }
                    }
                    
                    p.score = score;
                    if (p.alive) {
                        players_alive_per_team[p.team_number - 1] ++;
                        team_alive_score[p.team_number - 1] += score;
                    }
                    //prevent duplicates
                    score = score << 8 | p.player_id;
                    //we want high rankings out first
                    rankings.Add(-score, p);
                }
                
                //Determine Team Ranks
                for (int i = 0; i < 3; i++) {
                    MainClass.HostDebugWriteLine("Team " + (i + 1) + " Had " + players_alive_per_team[i] + " Players alive");
                    MainClass.HostDebugWriteLine("Combined Score: " + team_alive_score[i]);
                    team_final_score[i] = (players_alive_per_team[i] << 10)
                                        + (team_alive_score[i] << 2);
                    MainClass.HostDebugWriteLine("Final: Team " + (i + 1) + " Score " + team_final_score[i]);
                }
                for (int i = 0; i < 3; i++) {
                    team_rank[i] = 3;
                    if (team_final_score[i] >= team_final_score[(i + 1) % 3]) {
                        team_rank[i]--;
                    }
                    if (team_final_score[i] >= team_final_score[(i + 2) % 3]) {
                        team_rank[i]--;
                    }
                    MainClass.HostDebugWriteLine("Team " + (i + 1) + " Rank " + team_rank[i]);
                }

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
                    p.team_rank = team_rank[p.team_number - 1];
                }
                
                break;
            }
            default:
                break;
            }

        }
        
        private void Shoot(int team_number, int player_number, int damage, bool hosted)
        {
            if (!hosted) return;
            
            UInt16 shot = (UInt16)(((team_number & 0x3) << 5) 
                                   | ((player_number & 0x07) << 2)
                                   | (damage & 0x2));
            TransmitBytes(shot, 7);
            string debug = String.Format("Shot: {0:d},{1:d},{2:d} 0x{3:x}", team_number, player_number, damage, shot);
            MainClass.HostDebugWriteLine(debug);
        }
        
        private int team_number = 1;
        
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
                    
                    UInt16[] values = new UInt16[]{
                        (UInt16)game_state.game_type,
                        game_id,//Game ID
                        DecimalToDecimalHex(game_state.game_time_minutes), //game time minutes
                        DecimalToDecimalHex(game_state.tags), //tags
                        DecimalToDecimalHex(game_state.reloads), //reloads
                        DecimalToDecimalHex(game_state.sheild), //sheild
                        DecimalToDecimalHex(game_state.mega), //mega
                        flags, //unknown/team/medic/unknown
                        //[3 bits - b001 - unknown][1 bit - team tag][1 bit - medic mode][3 bits - 0x0 - unknown]
                        DecimalToDecimalHex(game_state.number_of_teams), //number of teams
                    };

                    TransmitPacket2(ref values);
                    
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
                    /**
                     * There does not appear to be a reason to tell the gun the number of players
                     * ahead of time.  It only prevents those players from joining midgame.  The
                     * score report is bitmasked and only reports non-zero scores.
                     */
                    UInt16[] values = new UInt16[]{
                        (UInt16)CommandCode.COMMAND_CODE_COUNTDOWN_TO_GAME_START,
                        game_id,//Game ID
                        DecimalToDecimalHex((byte)seconds_left),
                        0x08, //players on team 1
                        0x08, //players on team 2
                        0x08, //players on team 3
                    };
                    TransmitPacket2(ref values);
                    MainClass.HostDebugWriteLine("T-" + seconds_left);
                }
                break;
            }
            case HostingState.HOSTING_STATE_PLAYING:
            {
                if (now > playing_state.game_end) {
                    hosting_state = HostGun.HostingState.HOSTING_STATE_SUMMARY;
                    MainClass.HostDebugWriteLine("Game Over");
                } else if (Console.KeyAvailable) {
                    char input = (char)Console.Read();
                    
                    if (input >= '0' && input <= '7') {
                        int player_number = input - '0';
                        
                        int damage = 0;
                        Shoot(team_number, player_number, damage, true);
                    } else {
                        team_number = (team_number % 3) + 1;
                        MainClass.HostDebugWriteLine("Host gun set to shoot for team: " + team_number);
                    }
                }
                break;
            }
            case HostingState.HOSTING_STATE_SUMMARY:
            {
                if (now > summary_state.last_announce.AddSeconds(5)) {

                    Player next_debreif = null;
                    
                    foreach (Player p in players) {
                        if (!p.debriefed) {
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
                        PrintScoreReport();
                        break;
                    }
                    
                    UInt16 player_index = (UInt16)((next_debreif.team_number & 0xf) << 4 | (next_debreif.player_number & 0xf));
                    
                    summary_state.last_announce = now;
                    UInt16[] values = new UInt16[]{
                        (UInt16)CommandCode.COMMAND_CODE_SCORE_ANNOUNCEMENT,
                        game_id,//Game ID
                        player_index,//player index
                        // [ 4 bits - team ] [ 4 bits - player number ]
                        0x0F, //unknown
                    };
                    TransmitPacket2(ref values);
                }
                break;
            }
            case HostingState.HOSTING_STATE_GAME_OVER:
            {
                if (now > game_over_state.last_announce.AddSeconds(5)) {
                    
                    game_over_state.last_announce = now;
                    
                    foreach (Player p in players) {
                        
                        UInt16 player_index = (UInt16)((p.team_number & 0xf) << 4 | (p.player_number & 0xf));
                        
                        UInt16[] values = new UInt16[]{
                            (UInt16)CommandCode.COMMAND_CODE_GAME_OVER,
                            game_id,//Game ID
                            player_index,//player index
                            // [ 4 bits - team ] [ 4 bits - player number ]
                            (UInt16)p.individual_rank, //player rank (not decimal hex, 1-player_count)
                            (UInt16)p.team_rank, //team rank?
                            0x00,//unknown...
                            0x00,
                            0x00,
                            0x00,
                            0x00,
                        };
                        TransmitPacket2(ref values);
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
            
            //hg.RunRankTests();
            
            while (true) {
                
                hg.Update();
                
            }
        }
    }
}
