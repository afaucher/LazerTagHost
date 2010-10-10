
using System;
using System.Collections.Generic;
using LazerTagHostLibrary;

namespace LazerTagHostUI
{


    public partial class HostWindow : Gtk.Window, HostChangedListener, PlayerSelectionScreen.HostControlListener
    {
        private HostGun hg = null;
        private bool relative_scoresheet = false;

        public HostWindow (HostGun hg) : base(Gtk.WindowType.Toplevel)
        {
            this.Build ();

            GLib.TimeoutHandler th = new GLib.TimeoutHandler(HostUpdate);
            GLib.Timeout.Add(100,th);

            this.hg = hg;

            hg.AddListener(this);

            PlayerSelector ps = playerselectionscreenMain.GetPlayerSelector();

            if (hg.IsTeamGame()) {
                ps.SetColumnLabels("Team 1", "Team 2", "Team 3");
            } else {
                ps.SetColumnLabels("","","");
            }
            playerselectionscreenMain.SubscribeEvents(this);

            RefreshPlayerList();
        }

        private bool HostUpdate()
        {
            hg.Update();

            string title = hg.GetGameStateText() + "\n" + hg.GetCountdown();
            playerselectionscreenMain.SetTitle(title);
    
            return true;
        }

        private Player GetSelectedPlayer() {

            LazerTagHostUI.PlayerSelector ps = playerselectionscreenMain.GetPlayerSelector();
            if (ps == null) return null;

            uint team_index = 0;
            uint player_index = 0;
            bool found = ps.GetCurrentSelectedPlayer(ref team_index, ref player_index);
            if (!found) return null;

            Player found_player = hg.LookupPlayer((int)team_index + 1, (int)player_index);
            return found_player;
        }

        private string GetPlayerName(uint team_index, uint player_index) {
    
            Player found_player = hg.LookupPlayer((int)team_index + 1, (int)player_index);
    
            //Console.WriteLine("Set " + team_index + "," + player_index + " to " + (found ? "found" : "not found"));
            if (found_player == null) return "Open";
    
            string text = found_player.player_name;
    
            switch (hg.GetGameState()) {
            case HostGun.HostingState.HOSTING_STATE_SUMMARY:
                text += " / " + (found_player.HasBeenDebriefed() ? "Done" : "Waiting");
                break;
            case HostGun.HostingState.HOSTING_STATE_GAME_OVER:
                string postfix = "";
                switch (found_player.individual_rank) {
                    case 1:
                        postfix = "st";
                        break;
                    case 2:
                        postfix = "nd";
                        break;
                    case 3:
                        postfix = "rd";
                        break;
                    default:
                        postfix = "th";
                        break;
                }
                text += " / " + found_player.individual_rank + postfix;

                if (relative_scoresheet) {
                    Player selected_player = GetSelectedPlayer();
                    if (selected_player == found_player) {
                        text += "\nYou";
                    } else if (selected_player != null) {

                        text += "\nYou Hit: ";
                        text += selected_player.hit_by_team_player_count[team_index,player_index];
                        text += " Hit You: ";
                        text += selected_player.hit_team_player_count[team_index,player_index];
                    } else {
                        text += "\nUnknown";
                    }
                } else {
                    text += "\nScore: " + found_player.score + " Dmg Recv: " + found_player.damage;
                }

                break;
            }
            return text;
        }
    
        private void RefreshPlayerList()
        {
    

    
            LazerTagHostUI.PlayerSelector ps = playerselectionscreenMain.GetPlayerSelector();
            if (ps == null) return;
    
            ps.RefreshPlayerNames(new LazerTagHostUI.PlayerSelector.GetPlayerName(GetPlayerName));
        }

#region PlayerSelectionScreen.HostControlListener implmentation

        void PlayerSelectionScreen.HostControlListener.LateJoin (object sender, System.EventArgs e)
        {
            Console.WriteLine("LateJoin");
        }

        void PlayerSelectionScreen.HostControlListener.Next (object sender, System.EventArgs e)
        {
            Console.WriteLine("Next");

            hg.Next();
        }

        void PlayerSelectionScreen.HostControlListener.Pause(object sender, System.EventArgs e)
        {
            hg.Pause();
        }

        void PlayerSelectionScreen.HostControlListener.Abort (object sender, System.EventArgs e)
        {
            Console.WriteLine("Abort");
            hg.EndGame();

            this.Hide();
        }

        void PlayerSelectionScreen.HostControlListener.RenamePlayer (object sender, System.EventArgs e)
        {
            Console.WriteLine("Rename");
            Gtk.ComboBoxEntry entry = (Gtk.ComboBoxEntry)sender;

            if (entry == null) return;

            string name = entry.Entry.Text;

            if (name.Length < 3) return;

            //TODO, append to all
            entry.AppendText(name);

            LazerTagHostUI.PlayerSelector ps = playerselectionscreenMain.GetPlayerSelector();
            if (ps == null) return;

            uint team_index_out = 0;
            uint player_index_out = 0;
            bool found = ps.GetCurrentSelectedPlayer(ref team_index_out, ref player_index_out);

            if (!found) return;

            found = hg.SetPlayerName((int)team_index_out, (int)player_index_out, name);

            if (!found) return;
            RefreshPlayerList();
        }

        void PlayerSelectionScreen.HostControlListener.DropPlayer (object sender, System.EventArgs e)
        {
            Console.WriteLine("Drop");
            
    
            LazerTagHostUI.PlayerSelector ps = playerselectionscreenMain.GetPlayerSelector();
            if (ps == null) return;
    
            uint team_index_out = 0;
            uint player_index_out = 0;
            bool found = ps.GetCurrentSelectedPlayer(ref team_index_out, ref player_index_out);
    
            if (!found) return;
    
            hg.DropPlayer((int)team_index_out, (int)player_index_out);

            RefreshPlayerList();
        }

        void PlayerSelectionScreen.HostControlListener.SelectionChanged(uint team_index, uint player_index) {
        Console.WriteLine("t" + team_index + "p" + player_index);
            if (relative_scoresheet) {
                RefreshPlayerList();
            }
        }

        void PlayerSelectionScreen.HostControlListener.RelativeScoresToggle(bool show_relative) {
            relative_scoresheet = show_relative;
            RefreshPlayerList();
        }

#endregion
    
#region HostChangedListener implementation
        void HostChangedListener.PlayerListChanged(List<Player> players) {
            RefreshPlayerList();
        }

        void HostChangedListener.GameStateChanged(HostGun.HostingState state) {
            //drop,rename,late,abort,next
            switch (state) {
            case HostGun.HostingState.HOSTING_STATE_ADDING:
            case HostGun.HostingState.HOSTING_STATE_CONFIRM_JOIN:
                //Disable Late Join
                playerselectionscreenMain.SetControlOptions(true,true,false,true,true,true);
                break;
            case HostGun.HostingState.HOSTING_STATE_PLAYING:
                playerselectionscreenMain.SetControlOptions(true,true,true,true,true,false);
                //disable pause, next
                break;
            case HostGun.HostingState.HOSTING_STATE_GAME_OVER:
            case HostGun.HostingState.HOSTING_STATE_SUMMARY:
                playerselectionscreenMain.SetControlOptions(false,true,false,true,true,false);
                //disable pause, late join, drop
                break;

            case HostGun.HostingState.HOSTING_STATE_COUNTDOWN:
                playerselectionscreenMain.SetControlOptions(true,true,false,true,false,false);
                //disable next, LateJoin, pause

                break;
            default:
                playerselectionscreenMain.SetControlOptions(false,false,false,true,true,false);
                break;
            }
            RefreshPlayerList();
        }

#endregion
    }
}
