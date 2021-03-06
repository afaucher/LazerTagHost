using System;
using System.IO.Ports;
using System.Globalization;
using System.Collections.Generic;
using System.Collections;


namespace LazerTagHostLibrary
{
    public interface HostChangedListener
    {
        void PlayerListChanged(List<Player> players);
        void GameStateChanged(HostGun.HostingState state);
    }

    public class HostGun
    {
        static private void HostDebugWriteLine(string line)
        {
            DateTime now = DateTime.Now;
            Console.WriteLine("Host: (" + now + ") " + line);
        }

        private struct GameState {
            public CommandCode game_type;
            public byte game_time_minutes;
            public int game_start_countdown_seconds;
            public byte tags;
            public byte reloads;
            public byte sheild;
            public byte mega;
            public bool team_tag;
            public bool medic_mode;
            public byte number_of_teams;
        };

        private struct ConfirmJoinState {
            public byte player_id;
        };


        public enum HostingState {
            HOSTING_STATE_IDLE,
            HOSTING_STATE_ADDING,
            HOSTING_STATE_CONFIRM_JOIN,
            HOSTING_STATE_COUNTDOWN,
            HOSTING_STATE_PLAYING,
            HOSTING_STATE_SUMMARY,
            HOSTING_STATE_GAME_OVER,
        };

        //TODO: Export only game types
        public enum CommandCode {
            //Game Types
            COMMAND_CODE_CUSTOM_GAME_MODE_HOST = 0x02, //Solo
            COMMAND_CODE_2TMS_GAME_MODE_HOST = 0x03,
            COMMAND_CODE_3TMS_GAME_MODE_HOST = 0x04,
            COMMAND_CODE_HIDE_AND_SEEK_GAME_MODE_HOST = 0x05,
            COMMAND_CODE_HUNT_THE_PREY_GAME_MODE_HOST = 0x06,
            COMMAND_CODE_2_KINGS_GAME_MODE_HOST = 0x07,
            COMMAND_CODE_3_KINGS_GAME_MODE_HOST = 0x08,
            COMMAND_CODE_OWN_THE_ZONE_GAME_MODE_HOST = 0x09, //Solo
            COMMAND_CODE_2TMS_OWN_THE_ZONE_GAME_MODE_HOST = 0x0A,
            COMMAND_CODE_3TMS_OWN_THE_ZONE_GAME_MODE_HOST = 0x0B,
            COMMAND_CODE_SPECIAL_GAME_DEFINITION = 0x0C,

            COMMAND_CODE_PLAYER_JOIN_GAME_REQUEST = 0x10,
            COMMAND_CODE_ACK_PLAYER_JOIN_RESPONSE = 0x01,
            COMMAND_CODE_CONFIRM_PLAY_JOIN_GAME = 0x11,
            //TODO:COMMAND_CODE_RESEND_JOIN_CONFIRMATION
            //Host: (10/10/2010 1:56:43 AM) Command: (15) SEQ:f,79,be,
            COMMAND_CODE_RESEND_JOIN_CONFIRMATION = 0x0f,

            COMMAND_CODE_COUNTDOWN_TO_GAME_START = 0x00,

            COMMAND_CODE_SCORE_ANNOUNCEMENT = 0x31,
            //Score Reports
            COMMAND_CODE_PLAYER_REPORT_SCORE = 0x40,
            COMMAND_CODE_PLAYER_HIT_BY_TEAM_1_REPORT = 0x41,
            COMMAND_CODE_PLAYER_HIT_BY_TEAM_2_REPORT = 0x42,
            COMMAND_CODE_PLAYER_HIT_BY_TEAM_3_REPORT = 0x43,

            COMMAND_CODE_GAME_OVER = 0x32,

            COMMAND_CODE_TEXT_MESSAGE = 0x80,
        };
        
        private byte game_id = 0x00;
        
        private LazerTagSerial ltz = null;
        private const int ADDING_ADVERTISEMENT_INTERVAL_SECONDS = 3;
        private const int WAIT_FOR_ADDITIONAL_PLAYERS_TIMEOUT_SECONDS = 100;
        //private const int GAME_START_COUNTDOWN_INTERVAL_SECONDS = 10;
        private const int GAME_TIME_DURATION_MINUTES = 1;
        private const int MINIMUM_PLAYER_COUNT_START = 2;
        private const int GAME_START_COUNTDOWN_ADVERTISEMENT_INTERVAL_SECONDS = 1;
        //1s breaks
        private const int GAME_DEBREIF_ADVERTISEMENT_INTERVAL_SECONDS = 3;
        private const int GAME_OVER_ADVERTISEMENT_INTERVAL_SECONDS = 5;
        private const int INTER_PACKET_BYTE_DELAY_MILISECONDS = 100;
        private const int MAX_PLAYER_COUNT = 24;
        private bool autostart = false;
        
        //host gun state
        private GameState game_state;
        private HostingState hosting_state = HostGun.HostingState.HOSTING_STATE_IDLE;
        private bool paused;

        private ConfirmJoinState confirm_join_state;

        //loose change state
        private LinkedList<Player> players = new LinkedList<Player>();
        private HostChangedListener listener = null;
        private DateTime state_change_timeout;
        private DateTime next_announce;

#region SerialProtocol
        private List<IRPacket> incoming_packet_queue = new List<IRPacket>();
#endregion

#region TestData
        private int host_shot_team_number = 1;
#endregion
        
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

        public void SetGameStartCountdownTime(int game_start_countdown_seconds) {
            game_state.game_start_countdown_seconds = game_start_countdown_seconds;
        }

        private static string GetCommandCodeName(CommandCode code)
        {
            Enum c = code;
            return c.ToString();
        }

        private bool AssignTeamAndPlayer(int team_request, 
                                         Player dest_player)
        {
            int team_assignment = 0;
            int player_assignment = 0;

            if (players.Count >= MAX_PLAYER_COUNT) {
                HostDebugWriteLine("Max player count hit");
                return false;
            }

            switch (game_state.game_type) {
            //Solo Games
            case CommandCode.COMMAND_CODE_CUSTOM_GAME_MODE_HOST:
            case CommandCode.COMMAND_CODE_OWN_THE_ZONE_GAME_MODE_HOST:
                /**
                 * Cycle through all spots until we find an open one
                 */
                for (int team_index = 0; team_index < 3; team_index++) {
                    for (int player_number = 0; player_number < 8; player_number++) {
                        bool found = false;
                        foreach (Player p in players) {

                            if (p.team_index == team_index
                                && p.player_number == player_number) {
                                found = true;
                                break;
                            }
                        }
                        if (!found) {
                            dest_player.team_index = team_index;
                            dest_player.team_number = team_index + 1;
                            dest_player.player_number = player_number;
                            return true;
                        }

                    }
                }
                return false;
            //Team Games
            case CommandCode.COMMAND_CODE_2TMS_GAME_MODE_HOST:
            case CommandCode.COMMAND_CODE_3TMS_GAME_MODE_HOST:
            case CommandCode.COMMAND_CODE_HIDE_AND_SEEK_GAME_MODE_HOST:
            case CommandCode.COMMAND_CODE_HUNT_THE_PREY_GAME_MODE_HOST:
            case CommandCode.COMMAND_CODE_2_KINGS_GAME_MODE_HOST:
            case CommandCode.COMMAND_CODE_3_KINGS_GAME_MODE_HOST:
            case CommandCode.COMMAND_CODE_2TMS_OWN_THE_ZONE_GAME_MODE_HOST:
            case CommandCode.COMMAND_CODE_3TMS_OWN_THE_ZONE_GAME_MODE_HOST:
                /**
                 * We first locate the smallest team, then assign an unused player slot
                 */
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
                        HostDebugWriteLine("Unable to assign team");
                        return false;
                    }
                }
                break;
            default:
                HostDebugWriteLine("Unable to assign team");
                return false;
            }
            
            /*switch (game_state.game_type) {
            case CommandCode.COMMAND_CODE_CUSTOM_GAME_MODE_HOST:
            case CommandCode.COMMAND_CODE_2TMS_GAME_MODE_HOST:
            case CommandCode.COMMAND_CODE_3TMS_GAME_MODE_HOST:*/
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
                    HostDebugWriteLine("Failed to assign player number");
                    return false;
                }
                //break;
            }
            /*default:
                return false;
            }*/

            dest_player.team_number = team_assignment;
            dest_player.team_index = team_assignment - 1;
            dest_player.player_number = player_assignment;
            
            HostDebugWriteLine("Assigned player to team " + team_assignment + " and player " + player_assignment);
            
            return true;
        }

        private void AssertUnknownBits(String name, IRPacket data, byte mask)
        {
            if (((byte)data.data & mask) != 0x00) {
                string debug = String.Format("Unknown bits set: \"{0:s}\" unknown: {1:x} data: {2:x} mask {3:x}",
                                             name,
                                             (byte)data.data & mask,
                                             (byte)data.data,
                                             mask);
                HostDebugWriteLine(debug);
            }
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
            AssertUnknownBits("still_alive_packet",incoming_packet_queue[4],0xfe);

            IRPacket unknown_one = incoming_packet_queue[5];
            AssertUnknownBits("unknown_one",unknown_one,0xff);
            IRPacket zone_time_minutes_packet = incoming_packet_queue[6];
            IRPacket zone_time_seconds_packet = incoming_packet_queue[7];

            
            //[4 bits - zero - unknown][1 bit - hit by t3][1 bit - hit by t2][1 bit - hit by t1][1 bit - zero - unknown]
            IRPacket team_hit_report = incoming_packet_queue[8];
            AssertUnknownBits("team_hit_report",team_hit_report,0xf9);
            
            UInt16 confirmed_game_id = game_id_packet.data;
            UInt16 player_index = player_index_packet.data;
            
            if ((CommandCode)command_packet.data != CommandCode.COMMAND_CODE_PLAYER_REPORT_SCORE) {
                HostDebugWriteLine("Wrong command");
                return false;
            }
            
            if (game_id != confirmed_game_id) {
                HostDebugWriteLine("Wrong game id");
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
                    
                    p.alive = ((still_alive_packet.data & 0x01) == 0x01);
                    p.damage = DecimalHexToDecimal(damage_recv_packet.data);
                    p.has_score_report_for_team[0] = (team_hit_report.data & 0x2) != 0;
                    p.has_score_report_for_team[1] = (team_hit_report.data & 0x4) != 0;
                    p.has_score_report_for_team[2] = (team_hit_report.data & 0x8) != 0;
                    p.zone_time = DecimalHexToDecimal(zone_time_minutes_packet.data) * 60
                        + DecimalHexToDecimal(zone_time_seconds_packet.data);
                    
                    break;
                }
            }
            
            if (!found) {
                HostDebugWriteLine("Unable to find player for score report");
                return false;
            }
            
            string debug = String.Format("Debriefed team {0:d} player {1:d}", team_number, player_number);
            HostDebugWriteLine(debug);
            
            return true;
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
            /*AssertUnknownBits("incoming_packet_queue[4]",incoming_packet_queue[4],0xff);
            AssertUnknownBits("incoming_packet_queue[5]",incoming_packet_queue[5],0xff);
            AssertUnknownBits("incoming_packet_queue[6]",incoming_packet_queue[6],0xff);
            AssertUnknownBits("incoming_packet_queue[7]",incoming_packet_queue[7],0xff);
            AssertUnknownBits("incoming_packet_queue[8]",incoming_packet_queue[8],0xff);
            AssertUnknownBits("incoming_packet_queue[9]",incoming_packet_queue[9],0xff);*/
            
            //what team do the scores relate to hits from
            int team_index = ((int)command_packet.data - (int)CommandCode.COMMAND_CODE_PLAYER_HIT_BY_TEAM_1_REPORT);
            int player_number = 0;
            int team_number = 0;
            PlayerTeamNumberSplit(player_id_packet.data, ref player_number, ref team_number);

            if (game_id != game_id_packet.data) {
                HostDebugWriteLine("Wrong game id");
                return false;
            }

            Player p = LookupPlayer(team_number, player_number);
            if (p == null) {
                return false;
            }
            
            if (!p.has_score_report_for_team[team_index]) {
                HostDebugWriteLine("Score report already reported");
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
                    HostDebugWriteLine("Ran off end of score report");
                    return false;
                }
                
                IRPacket score_packet = incoming_packet_queue[score_index];
                
                p.hit_by_team_player_count[team_index,i] = DecimalHexToDecimal(score_packet.data);
                Player shooter = LookupPlayer(team_index + 1,i);
                if (shooter == null) {
                    continue;
                }
                string debug = String.Format("Hit: {0:d},{1:d}", p.team_number - 1, p.player_number);
                HostDebugWriteLine(debug);
                shooter.hit_team_player_count[p.team_index, p.player_number] = DecimalHexToDecimal(score_packet.data);
                
                score_index++;
            }

            if (listener != null) {
                listener.PlayerListChanged(new List<Player>(players));
            }
            
            return true;
        }
        
        private void PrintScoreReport()
        {
            foreach (Player p in players)
            {
                string debug = String.Format("Player 0x{0:x} {1:d},{2:d}",
                                             p.player_id, p.team_number, p.player_number);
                HostDebugWriteLine(debug);
                debug = String.Format("\tRank: {0:d} Team Rank: {1:d} Score: {2:d}", p.individual_rank, p.team_rank, p.score);
                HostDebugWriteLine(debug);
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
                        HostDebugWriteLine(debug);
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
                        HostDebugWriteLine(debug);
                    }
                }
            }
        }

        private bool ProcessCommandSequence()
        {
            DateTime now = DateTime.Now;
            
            switch (hosting_state) {
            case HostingState.HOSTING_STATE_IDLE:
            {
#if JOIN_PLAYERS_TEST
                if (!autostart) {
                    IRPacket command_packet = incoming_packet_queue[0];
                    IRPacket game_id_packet = incoming_packet_queue[1];



                    if (command_packet.data == (UInt16)CommandCode.COMMAND_CODE_CUSTOM_GAME_MODE_HOST) {
                        System.Threading.Thread.Sleep(100);
                        byte game_id = (byte)game_id_packet.data;
                        if (join_count < 100) {
                            join_count++;
                            if (join_count % 10 == 0) {
                                SendPlayerJoin(game_id);
                            }
                        }
                    } else if (command_packet.data == (UInt16)CommandCode.COMMAND_CODE_ACK_PLAYER_JOIN_RESPONSE) {
                        System.Threading.Thread.Sleep(100);

                        IRPacket player_id_packet = incoming_packet_queue[2];

                        UInt16[] join = {
                            (UInt16)CommandCode.COMMAND_CODE_CONFIRM_PLAY_JOIN_GAME,
                            game_id_packet.data,
                            player_id_packet.data,
                        };
                        TransmitPacket2(ref join);

                    }
                }
#endif

                return true;
            }
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
                    HostDebugWriteLine("Wrong command");
                    return false;
                }
                
                if (game_id != game_id_packet.data) {
                    HostDebugWriteLine("Wrong game id");
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
                    HostDebugWriteLine("Player id collision");
                    return false;
                }
                
                confirm_join_state.player_id = (byte)player_id;
                
                /* 
                 * 0 = any
                 * 1-3 = team 1-3
                 */
                UInt16 team_request = (UInt16)(player_team_request_packet.data & 0x03);

                Player p = new Player((byte)player_id);

                if (!AssignTeamAndPlayer(team_request,
                                    p))
                {
                    return false;
                }
                
                players.AddLast(p);
                
                UInt16 team_response = (UInt16)((p.team_number << 3) | (p.player_number));
                
                
                UInt16[] values = new UInt16[]{
                    (UInt16)CommandCode.COMMAND_CODE_ACK_PLAYER_JOIN_RESPONSE,
                    game_id,//Game ID
                    player_id,//Player ID
                    team_response, //player #
                    // [3 bits - zero - unknown][2 bits - team assignment][3 bits - player assignment]
                };
                
                if (game_id_packet.data != game_id) {
                    HostDebugWriteLine("Game id does not match current game, discarding");
                    return false;
                }
                
                string debug = String.Format("Player {0:x} found, joining", new object[] { player_id });
                HostDebugWriteLine(debug);
                
                TransmitPacket2(ref values);

                incoming_packet_queue.Clear();
                
                hosting_state = HostGun.HostingState.HOSTING_STATE_CONFIRM_JOIN;
                state_change_timeout = now.AddSeconds(2);
                
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
                    HostDebugWriteLine("Wrong command");
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
                    HostDebugWriteLine("Invalid confirmation: " + debug);
                    ChangeState(now, HostingState.HOSTING_STATE_ADDING);
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
                    HostDebugWriteLine("Confirmed player");
                } else {
                    HostDebugWriteLine("Unable to find player to confirm");
                    return false;
                }
                
                if (players.Count >= MINIMUM_PLAYER_COUNT_START) {

                    state_change_timeout = now.AddSeconds(WAIT_FOR_ADDITIONAL_PLAYERS_TIMEOUT_SECONDS);
                }
                ChangeState(now, HostingState.HOSTING_STATE_ADDING);
                incoming_packet_queue.Clear();
                if (listener != null) {
                    listener.PlayerListChanged(new List<Player>(players));
                }
                
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

        private bool ProcessPacket(IRPacket.PacketType type, UInt16 data, UInt16 number_of_bits)
        {
            //DateTime now = DateTime.Now;
            
            if (type != IRPacket.PacketType.PACKET_TYPE_LTX) return false;
            
            if (number_of_bits == 9)
            {
                if ((data & 0x100) != 0) {
                    //end sequence
                    if ((data & 0xff) == LazerTagSerial.ComputeChecksum(ref incoming_packet_queue)) {
                        HostDebugWriteLine("Command: (" 
                                                     + GetCommandCodeName((CommandCode)(incoming_packet_queue[0].data)) 
                                                     + ") " 
                                                     + SerializeCommandSequence(ref incoming_packet_queue));
                        if (!ProcessCommandSequence()) {
                            HostDebugWriteLine("ProcessCommandSequence failed: " + SerializeCommandSequence(ref incoming_packet_queue));
                        }
                    } else {
                        HostDebugWriteLine("Invalid Checksum SEQ: " + SerializeCommandSequence(ref incoming_packet_queue));
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
                HostDebugWriteLine("Unknown packet, clearing queue");
                incoming_packet_queue.Clear();
            } else {
                string debug = String.Format(type.ToString() + " {0:x}, {1:d}",data, number_of_bits);
                HostDebugWriteLine(debug);
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
                if (parameters.Length != 2) {
                    return false;
                }
                
                UInt16 data = UInt16.Parse(parameters[0], NumberStyles.AllowHexSpecifier);
                UInt16 number_of_bits = UInt16.Parse(parameters[1]);

                HostDebugWriteLine(String.Format("LTTO: {0:d},{1:x}", number_of_bits, data));
                break;
            default:
                break;
            }
            
            return false;
        }

#region SerialProtocol

        private void TransmitPacket2(ref UInt16[] values)
        {
            ltz.TransmitPacket(ref values);
        }

        private void TransmitBytes(UInt16 data, UInt16 number_of_bits)
        {
            ltz.EnqueueLTX(data,number_of_bits);
        }

        private void TransmitLTTOBytes(UInt16 data, UInt16 number_of_bits)
        {
            ltz.EnqueueLTTO(data,number_of_bits);
        }

        static private string SerializeCommandSequence(ref List<IRPacket> packets)
        {
            string command = "SEQ:";
            foreach (IRPacket packet in packets) {
                command += String.Format("{0:x},", packet.data);
            }
            return command;
        }

        static private byte DecimalToDecimalHex(byte d)
        {
            //Unlimited
            if (d == 0xff) return d;
            //Otherwise
            byte result = (byte)(((d / 10) << 4) | (d % 10));
            return result;
        }
        
        static private int DecimalHexToDecimal(int d)
        {
            int ret = d & 0x0f;
            ret += ((d >> 4) & 0xf) * 10;
            return ret;
        }

#endregion

        private void RankPlayers()
        {
            switch (game_state.game_type) {
            case CommandCode.COMMAND_CODE_OWN_THE_ZONE_GAME_MODE_HOST:
            case CommandCode.COMMAND_CODE_2TMS_OWN_THE_ZONE_GAME_MODE_HOST:
            case CommandCode.COMMAND_CODE_3TMS_OWN_THE_ZONE_GAME_MODE_HOST:
            {
                SortedList<int, Player> rankings =
                    new SortedList<int, Player>();
                //for team score
                int[] team_zone_time = new int[3] { 0,0,0, };
                //rank for each team
                int[] team_rank = new int[3] { 0,0,0, };

                foreach (Player p in players) {
                    p.score = p.zone_time;

                    int score = p.zone_time;
                    score = (int.MaxValue - score) << 8 | p.player_id;
                    rankings.Add(score, p);

                    team_zone_time[p.team_index] += p.zone_time;
                }

                //Determine PlayerRanks
                //TODO: Check if this sort order needs reversing
                int rank = 0;
                int last_score = 99;
                foreach(KeyValuePair<int, Player> e in rankings) {

                    Player p = e.Value;
                    if (p.score != last_score) {
                        rank++;
                        last_score = p.score;
                    }
                    p.individual_rank = rank;
                    p.team_rank = 0;
                }

                //Team rank only for team games
                switch (game_state.game_type) {
                case CommandCode.COMMAND_CODE_2TMS_OWN_THE_ZONE_GAME_MODE_HOST:
                case CommandCode.COMMAND_CODE_3TMS_OWN_THE_ZONE_GAME_MODE_HOST:
                {
                    for (int i = 0; i < 3; i++) {
                        team_rank[i] = 3;
                        if (team_zone_time[i] >= team_zone_time[(i + 1) % 3]) {
                            team_rank[i]--;
                        }
                        if (team_zone_time[i] >= team_zone_time[(i + 2) % 3]) {
                            team_rank[i]--;
                        }
                        HostDebugWriteLine("Team " + (i + 1) + " Rank " + team_rank[i]);
                    }

                    foreach (Player p in players) {
                        p.team_rank = team_rank[p.team_index];
                    }

                    break;
                }
                default:
                    break;
                }

                break;
            }
            case CommandCode.COMMAND_CODE_2TMS_GAME_MODE_HOST:
            case CommandCode.COMMAND_CODE_3TMS_GAME_MODE_HOST:
            case CommandCode.COMMAND_CODE_HUNT_THE_PREY_GAME_MODE_HOST: //Untested
            case CommandCode.COMMAND_CODE_HIDE_AND_SEEK_GAME_MODE_HOST: //Untested
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
                    //TODO: test to make sure scoring works accurately
                    for (team_index = 0; team_index < 3; team_index++) {
                        for (player_index = 0; player_index < 8; player_index++) {
                            if (p.team_index == team_index) {
                                //Friendly fire
                                score -= 2 * p.hit_team_player_count[team_index, player_index];
                            } else {
                                score += 2 * p.hit_team_player_count[team_index, player_index];
                            }
                        }
                    }
                    
                    p.score = score;
                    if (p.alive) {
                        players_alive_per_team[p.team_index] ++;
                        team_alive_score[p.team_index] += score;
                    }
                    //prevent duplicates
                    score = score << 8 | p.player_id;
                    //we want high rankings out first
                    rankings.Add(-score, p);
                }
                
                //Determine Team Ranks
                for (int i = 0; i < 3; i++) {
                    HostDebugWriteLine("Team " + (i + 1) + " Had " + players_alive_per_team[i] + " Players alive");
                    HostDebugWriteLine("Combined Score: " + team_alive_score[i]);
                    team_final_score[i] = (players_alive_per_team[i] << 10)
                                        + (team_alive_score[i] << 2);
                    HostDebugWriteLine("Final: Team " + (i + 1) + " Score " + team_final_score[i]);
                }
                for (int i = 0; i < 3; i++) {
                    team_rank[i] = 3;
                    if (team_final_score[i] >= team_final_score[(i + 1) % 3]) {
                        team_rank[i]--;
                    }
                    if (team_final_score[i] >= team_final_score[(i + 2) % 3]) {
                        team_rank[i]--;
                    }
                    HostDebugWriteLine("Team " + (i + 1) + " Rank " + team_rank[i]);
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
                    p.team_rank = team_rank[p.team_index];
                }
                
                break;
            }
            case CommandCode.COMMAND_CODE_2_KINGS_GAME_MODE_HOST:
            case CommandCode.COMMAND_CODE_3_KINGS_GAME_MODE_HOST:
            {
                //TODO: Write kings score
                break;
            }
            default:
                HostDebugWriteLine("Unable to score match");
                break;
            }

        }

#region TestData
        private void Shoot(int team_number, int player_number, int damage, bool hosted)
        {
            if (!hosted) return;
            
            UInt16 shot = (UInt16)(((team_number & 0x3) << 5) 
                                   | ((player_number & 0x07) << 2)
                                   | (damage & 0x2));
            TransmitBytes(shot, 7);
            string debug = String.Format("Shot: {0:d},{1:d},{2:d} 0x{3:x}", team_number, player_number, damage, shot);
            HostDebugWriteLine(debug);
        }

        private void SendPlayerJoin(byte game_id) {

            UInt16 player_id = (UInt16)(new Random().Next() & 0xff);

            HostDebugWriteLine("Joining " + player_id);

            UInt16[] join = {
                (UInt16)CommandCode.COMMAND_CODE_PLAYER_JOIN_GAME_REQUEST,
                game_id,
                player_id,
                0x09,
            };
            TransmitPacket2(ref join);


        }
#endregion

        private bool ChangeState(DateTime now, HostingState state) {

            paused = false;
            //TODO: Clear timeouts

            switch (state) {
            case HostingState.HOSTING_STATE_IDLE:
                players.Clear();
                break;
            case HostingState.HOSTING_STATE_COUNTDOWN:
                if (hosting_state != HostGun.HostingState.HOSTING_STATE_ADDING) return false;
                HostDebugWriteLine("Starting countdown");
                state_change_timeout = now.AddSeconds(game_state.game_start_countdown_seconds);
                break;
            case HostingState.HOSTING_STATE_ADDING:
                HostDebugWriteLine("Joining players");
                incoming_packet_queue.Clear();
                break;
            case HostingState.HOSTING_STATE_PLAYING:
                HostDebugWriteLine("Starting Game");
                state_change_timeout = now.AddMinutes(game_state.game_time_minutes);
                incoming_packet_queue.Clear();
                break;
            case HostingState.HOSTING_STATE_SUMMARY:
                HostDebugWriteLine("Debriefing");
                break;
            case HostingState.HOSTING_STATE_GAME_OVER:
                HostDebugWriteLine("Debreif Done");
                break;
            default:
                return false;
            }

            hosting_state = state;
            next_announce = now;

            if (listener != null) {
                listener.GameStateChanged(state);
            }

            return true;
        }

#region PublicInterface

        public void DynamicHostMode(CommandCode game_type,
                                    byte game_time_minutes,
                                    byte tags,
                                    byte reloads,
                                    byte sheilds,
                                    byte mega,
                                    bool team_tag,
                                    bool medic_mode,
                                    byte number_of_teams)
        {
            game_state.game_type = game_type;
            BaseGameSet(game_time_minutes,
                        tags,
                        reloads,
                        sheilds,
                        mega,
                        team_tag,
                        medic_mode);
            game_state.number_of_teams = number_of_teams;
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


        public Player LookupPlayer(int team_number, int player_number)
        {
            foreach (Player p in players) {
                if (p.team_number == team_number
                    && p.player_number == player_number)
                {
                    return p;
                }
            }
            //HostDebugWriteLine("Unable to lookup player " + team_number + "," + player_number);
            return null;
        }


        public void StartServer() {
            if (hosting_state != HostGun.HostingState.HOSTING_STATE_IDLE) return;

            ChangeState(DateTime.Now, HostGun.HostingState.HOSTING_STATE_ADDING);
        }

        public void EndGame() {
            ChangeState(DateTime.Now, HostGun.HostingState.HOSTING_STATE_IDLE);

        }

        public void DelayGame(int seconds) {
            state_change_timeout.AddSeconds(seconds);
        }

        public void Pause() {
            switch (hosting_state) {
            case HostingState.HOSTING_STATE_ADDING:
                paused = true;
                break;
            default:
                HostDebugWriteLine("Pause not enabled right now");
                break;
            }
        }

        public void Next() {
            DateTime now = DateTime.Now;
            switch (hosting_state) {
            case HostingState.HOSTING_STATE_ADDING:
                ChangeState(now, HostingState.HOSTING_STATE_COUNTDOWN);
                break;
            case HostingState.HOSTING_STATE_PLAYING:
                ChangeState(now, HostingState.HOSTING_STATE_SUMMARY);
                break;
            case HostingState.HOSTING_STATE_SUMMARY:
                ChangeState(now, HostingState.HOSTING_STATE_GAME_OVER);
                break;
            default:
                HostDebugWriteLine("Next not enabled right now");
                break;
            }
        }

        public bool StartGameNow() {
            return ChangeState(DateTime.Now, HostingState.HOSTING_STATE_COUNTDOWN);
        }



        public string GetGameStateText()
        {
            switch (hosting_state) {
            case HostingState.HOSTING_STATE_ADDING:
                return "Adding Players";
            case HostingState.HOSTING_STATE_COUNTDOWN:
                return "Countdown to game start";
            case HostingState.HOSTING_STATE_PLAYING:
                return "Game in progress";
            case HostingState.HOSTING_STATE_SUMMARY:
                return "Debriefing Players";
            case HostingState.HOSTING_STATE_GAME_OVER:
                return "Game Over";
            case HostingState.HOSTING_STATE_IDLE:
            default:
                    return "Not in a game";
            }
        }

        public string GetCountdown()
        {
            DateTime now = DateTime.Now;
            string countdown;

            if (state_change_timeout < now || paused) {

                countdown = "Waiting";

                switch (hosting_state) {
                case HostingState.HOSTING_STATE_ADDING:
                    int needed = (MINIMUM_PLAYER_COUNT_START - players.Count);
                    if (needed > 0) {
                        countdown = "Waiting for " + needed + " more players";
                    } else {
                        countdown = "Ready to start";
                    }
                    break;
                case HostingState.HOSTING_STATE_SUMMARY:
                    countdown += " for all players to check in";
                    break;
                case HostingState.HOSTING_STATE_GAME_OVER:
                    countdown = "All players may now receive scores";
                    break;
                default:
                    break;
                }


            } else {
                countdown = ((int)((state_change_timeout - now).TotalSeconds)).ToString() + " seconds";

                switch (hosting_state) {
                case HostingState.HOSTING_STATE_ADDING:
                    int needed = (MINIMUM_PLAYER_COUNT_START - players.Count);
                    if (needed > 0) {
                        countdown = "Waiting for " + needed + " more players";
                    } else {
                        countdown += " till countdown";
                    }
                    break;
                case HostingState.HOSTING_STATE_COUNTDOWN:
                    countdown += " till game start";
                    break;
                case HostingState.HOSTING_STATE_PLAYING:
                    countdown += " till game end";
                    break;
                default:
                    break;
                }

            }
            return countdown;
        }

        public bool SetPlayerName(int team_index, int player_index, string name)
        {
            Player p = LookupPlayer(team_index + 1, player_index);

            if (p == null) {
                HostDebugWriteLine("Player not found");
                return false;
            }

            p.player_name = name;

            return true;
        }

        public bool DropPlayer(int team_index, int player_index)
        {
            Player p = LookupPlayer(team_index + 1, player_index);

            if (p == null) {
                HostDebugWriteLine("Player not found");
                return false;
            }

            players.Remove(p);
            if (listener != null) {
                listener.PlayerListChanged(new List<Player>(players));
            }

            return false;
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

        public void Update() {
            DateTime now = DateTime.Now;
            
            if (ltz != null) {
                string input = ltz.TryReadCommand();
                
                int command_length = input.IndexOf(':');
                if (command_length > 0) {
                    string command = input.Substring(0,command_length);
                    
                    
                    string paramters_line = input.Substring(command_length + 2);
                    string[] paramters = paramters_line.Split(',');
                    
                    ProcessMessage(command, paramters);
                } else {
                    HostDebugWriteLine("DEBUG: " + input);
                }
            }
            
            switch (hosting_state) {
            case HostingState.HOSTING_STATE_IDLE:
            {
                //TODO
                if (autostart) {
                    Init2TeamHostMode(GAME_TIME_DURATION_MINUTES,10,0xff,15,10,true,false);
                    ChangeState(now, HostingState.HOSTING_STATE_ADDING);
                }
                break;
            }
            case HostingState.HOSTING_STATE_ADDING:
            {
                if (now.CompareTo(next_announce) > 0) {
                    
                    incoming_packet_queue.Clear();

                    bool extended_tagging = false;
                    bool unlimited_ammo = game_state.reloads == 0xff;
                    HostDebugWriteLine("unlimited_ammo " + game_state.reloads + ", " + unlimited_ammo);
                    bool unlimited_mega = game_state.mega == 0xff;
                    HostDebugWriteLine("unlimited_mega " + game_state.mega + ", " + unlimited_mega);
                    bool friendly_fire = game_state.team_tag;
                    bool medic_mode = game_state.medic_mode;
                    bool rapid_tags = false;
                    bool hunters_hunted = false;
                    bool hunters_hunted_direction = false;

                    bool zones = false;
                    bool bases_are_teams = false;
                    bool tagged_players_are_disabled = false;
                    bool base_areas_revive_players = false;
                    bool base_areas_are_hospitals = false;
                    bool base_areas_fire_at_players = false;

                    switch (game_state.game_type) {
                    case CommandCode.COMMAND_CODE_2TMS_GAME_MODE_HOST:
                    case CommandCode.COMMAND_CODE_3TMS_GAME_MODE_HOST:
                    case CommandCode.COMMAND_CODE_CUSTOM_GAME_MODE_HOST:
                    case CommandCode.COMMAND_CODE_3_KINGS_GAME_MODE_HOST:
                    case CommandCode.COMMAND_CODE_2_KINGS_GAME_MODE_HOST:
                        break;
                    case CommandCode.COMMAND_CODE_OWN_THE_ZONE_GAME_MODE_HOST:
                    case CommandCode.COMMAND_CODE_2TMS_OWN_THE_ZONE_GAME_MODE_HOST:
                    case CommandCode.COMMAND_CODE_3TMS_OWN_THE_ZONE_GAME_MODE_HOST:
                        zones = true;
                        tagged_players_are_disabled = true;
                        friendly_fire = true;
                        medic_mode = true;
                        break;
                    case CommandCode.COMMAND_CODE_HIDE_AND_SEEK_GAME_MODE_HOST:
                    case CommandCode.COMMAND_CODE_HUNT_THE_PREY_GAME_MODE_HOST:
                        hunters_hunted = true;
                        break;
                    default:
                        break;
                    }

                    UInt16[] values = PacketPacker.packGameDefinition(game_state.game_type,
                        game_id,
                        game_state.game_time_minutes,
                        game_state.tags,
                        game_state.reloads,
                        game_state.sheild,
                        game_state.mega,

                        extended_tagging,
                        unlimited_ammo,
                        unlimited_mega,
                        friendly_fire,
                        medic_mode,
                        rapid_tags,
                        hunters_hunted,
                        hunters_hunted_direction,

                        zones,
                        bases_are_teams,
                        tagged_players_are_disabled,
                        base_areas_revive_players,
                        base_areas_are_hospitals,
                        base_areas_fire_at_players,

                        game_state.number_of_teams,
                        null); //new char[] {'D','U','C','K'}


                    TransmitPacket2(ref values);
                    
                    next_announce = now.AddSeconds(ADDING_ADVERTISEMENT_INTERVAL_SECONDS);
                } else if (players.Count >= MINIMUM_PLAYER_COUNT_START
                           && now > state_change_timeout
                           && !paused)
                {
                    ChangeState(now, HostingState.HOSTING_STATE_COUNTDOWN);
                }
                break;
            }
            case HostingState.HOSTING_STATE_CONFIRM_JOIN:
            {
                if (now.CompareTo(state_change_timeout) > 0) {
                    //TODO: Use COMMAND_CODE_RESEND_JOIN_CONFIRMATION
                    HostDebugWriteLine("No confirmation on timeout");
                    ChangeState(now, HostingState.HOSTING_STATE_ADDING);
                }
                break;
            }
            case HostingState.HOSTING_STATE_COUNTDOWN:
            {
                if (state_change_timeout < now) {
                    ChangeState(now, HostingState.HOSTING_STATE_PLAYING);
                } else if (next_announce < now) {
                    next_announce = now.AddSeconds(1);
                    
                    int seconds_left = (state_change_timeout - now).Seconds;
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
                    HostDebugWriteLine("T-" + seconds_left);
                }
                break;
            }
            case HostingState.HOSTING_STATE_PLAYING:
            {
                if (now > state_change_timeout) {
                    ChangeState(now, HostingState.HOSTING_STATE_SUMMARY);
                } else if (now >= next_announce) {

                    switch (game_state.game_type) {
                    case CommandCode.COMMAND_CODE_OWN_THE_ZONE_GAME_MODE_HOST:
                    case CommandCode.COMMAND_CODE_2TMS_OWN_THE_ZONE_GAME_MODE_HOST:
                    case CommandCode.COMMAND_CODE_3TMS_OWN_THE_ZONE_GAME_MODE_HOST:
                        TransmitLTTOBytes(0x02,5);
                        next_announce = now.AddMilliseconds(500);
                        break;
                    default:
                        break;
                    }
#region TestData
                    //TODO: Won't get called if next announce is not advanced
                } else if (Console.KeyAvailable) {
                    char input = (char)Console.Read();
                    
                    if (input >= '0' && input <= '7') {
                        int player_number = input - '0';
                        
                        int damage = 0;
                        Shoot(host_shot_team_number, player_number, damage, true);
                    } else {
                        host_shot_team_number = (host_shot_team_number % 3) + 1;
                        HostDebugWriteLine("Host gun set to shoot for team: " + host_shot_team_number);
                    }
                }
#endregion
                break;
            }
            case HostingState.HOSTING_STATE_SUMMARY:
            {
                if (now > next_announce) {

                    Player next_debreif = null;

                    //pull players off the debreif list at random
                    {
                        List<Player> undebreifed = new List<Player>();
                        foreach (Player p in players) {
                            if (!p.HasBeenDebriefed()) {
                                undebreifed.Add(p);
                            }
                        }

                        if (undebreifed.Count > 0) {
                             next_debreif = undebreifed[new Random().Next() % undebreifed.Count];
                        }
                    }

                    if (next_debreif == null) {
                        HostDebugWriteLine("All players breifed");

                        RankPlayers();
                        PrintScoreReport();

                        ChangeState(now, HostingState.HOSTING_STATE_GAME_OVER);
                        break;
                    }
                    
                    UInt16 player_index = (UInt16)((next_debreif.team_number & 0xf) << 4 | (next_debreif.player_number & 0xf));
                    
                    next_announce = now.AddSeconds(GAME_DEBREIF_ADVERTISEMENT_INTERVAL_SECONDS);
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
                if (now > next_announce) {
                    
                    next_announce = now.AddSeconds(GAME_OVER_ADVERTISEMENT_INTERVAL_SECONDS);
                    
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


        public void AddListener(HostChangedListener listener) {
            this.listener = listener;
        }

        public HostGun(string device, HostChangedListener l) {
            ltz = new LazerTagSerial(device);
            this.listener = l;
        }

        public bool SetDevice(string device) {
            if (ltz != null) {
                ltz.Stop();
                ltz = null;
            }
            if (device != null) {
                try {
                    ltz = new LazerTagSerial(device);
                } catch (Exception ex) {
                    HostDebugWriteLine(ex.ToString());
                    return false;
                }
            }
            return true;
        }

        public HostingState GetGameState() {
            return hosting_state;
        }

        public bool IsTeamGame() {
            switch (game_state.game_type) {
            case CommandCode.COMMAND_CODE_CUSTOM_GAME_MODE_HOST:
            case CommandCode.COMMAND_CODE_OWN_THE_ZONE_GAME_MODE_HOST:
                return false;
            case CommandCode.COMMAND_CODE_2TMS_GAME_MODE_HOST:
            case CommandCode.COMMAND_CODE_3TMS_GAME_MODE_HOST:
            case CommandCode.COMMAND_CODE_HIDE_AND_SEEK_GAME_MODE_HOST:
            case CommandCode.COMMAND_CODE_HUNT_THE_PREY_GAME_MODE_HOST:
            case CommandCode.COMMAND_CODE_2_KINGS_GAME_MODE_HOST:
            case CommandCode.COMMAND_CODE_3_KINGS_GAME_MODE_HOST:
            case CommandCode.COMMAND_CODE_2TMS_OWN_THE_ZONE_GAME_MODE_HOST:
            case CommandCode.COMMAND_CODE_3TMS_OWN_THE_ZONE_GAME_MODE_HOST:
                return true;
            default:
                HostDebugWriteLine("Unknown game type");
                return false;
            }
        }

        public bool IsZoneGame() {
            switch (game_state.game_type) {
            case CommandCode.COMMAND_CODE_2TMS_OWN_THE_ZONE_GAME_MODE_HOST:
            case CommandCode.COMMAND_CODE_3TMS_OWN_THE_ZONE_GAME_MODE_HOST:
            case CommandCode.COMMAND_CODE_OWN_THE_ZONE_GAME_MODE_HOST:
                return true;
            default:
                return false;
            }
        }

#endregion
    }
}
