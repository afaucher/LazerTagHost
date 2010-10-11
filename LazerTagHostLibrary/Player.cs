
using System;

namespace LazerTagHostLibrary
{


    public class Player {
        public byte player_id;
        public bool confirmed = false;
        public bool debriefed = false;
        //0 = solo
        //1-3 = team 1-3
        public int team_number;
        public int team_index;
        //0-7 = player 0-7
        public int player_number;

        //damage taken during match
        public int damage = 0;
        //still alive at end of match
        public bool alive = false;
        //true if the debreifing stated a report was coming but one not received yet
        public bool[] has_score_report_for_team = new bool[3] {false, false, false};
        public int[,] hit_by_team_player_count = new int[3,8];
        public int[,] hit_team_player_count = new int[3,8];

        public int zone_time;
        
        //final score for given game mode
        public int score = 0;
        public int individual_rank = 0; //1-24
        public int team_rank = 0; //1-3

        public string player_name = "Unnamed Player";
        
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
}
