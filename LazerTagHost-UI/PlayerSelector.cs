
using System;

namespace LazerTagHostUI
{


    [System.ComponentModel.ToolboxItem(true)]
    public partial class PlayerSelector : Gtk.Bin
    {

        private SelectionChanged listener = null;
        private Gtk.RadioButton[,] radiobuttonPlayers = new Gtk.RadioButton[3,8];

        public PlayerSelector ()
        {
            this.Build ();

            Gtk.RadioButton first = null;

            for (uint team_index = 0; team_index < 3; team_index++) {
                for (uint player_index = 0; player_index < 8; player_index++) {
                    string name = "radiobutton_" + team_index + "_" + player_index;
    
                    Gtk.RadioButton rb = new Gtk.RadioButton(first, name);
                    radiobuttonPlayers[team_index,player_index] = rb;
    
                    if (first == null) first = rb;
    
                    rb.CanFocus = true;
                    rb.Name = name;
                    rb.DrawIndicator = false;
                    rb.UseUnderline = true;
                    rb.Active = (team_index == 0 && player_index == 0);
                    rb.Label = "";
                    rb.Clicked += new System.EventHandler(ListenSelectionChanges);
                    //TODO: Store index in rb.data
    
                    tablePlayerSelector.Add(rb);
                    Gtk.Table.TableChild tc = ((Gtk.Table.TableChild)(tablePlayerSelector[rb]));
                    uint top_offset = 1;
                    tc.TopAttach = player_index + top_offset + 0;
                    tc.BottomAttach = player_index + top_offset + 1;
                    uint left_offset = 0;
                    tc.LeftAttach = team_index + left_offset + 0;
                    tc.RightAttach = team_index + left_offset + 1;

                    

                }
            }

            this.ShowAll();
        }

        private void ListenSelectionChanges(object sender, System.EventArgs e) {
            if (listener == null) return;

            uint team_index = 0;
            uint player_index = 0;
            if (GetCurrentSelectedPlayer(ref team_index, ref player_index)) {

                listener(team_index, player_index);

            }
        }

        public void SetListener(SelectionChanged l) {
            listener = l;

            ListenSelectionChanges(null, null);
        }

        public delegate void SelectionChanged(uint team_index, uint player_index);


        public bool GetCurrentSelectedPlayer(ref uint team_index_out, ref uint player_index_out) {
            for (uint team_index = 0; team_index < 3; team_index++) {
                for (uint player_index = 0; player_index < 8; player_index++) {
                    Gtk.RadioButton rb = radiobuttonPlayers[team_index,player_index];
                    if (!rb.Active) continue;
    
                    team_index_out = team_index;
                    player_index_out = player_index;
    
                    return true;
                }
            }
            return false;
        }

        public delegate string GetPlayerName(uint team_index, uint player_index);

        public void RefreshPlayerNames(GetPlayerName namer) {

            if (namer == null) return;

            for (uint team_index = 0; team_index < 3; team_index++) {
                for (uint player_index = 0; player_index < 8; player_index++) {
                    Gtk.RadioButton rb = radiobuttonPlayers[team_index,player_index];
                    //if (!rb.Active) continue;

                    rb.Label = namer(team_index, player_index);
                }
            }
        }

        public void SetColumnLabels(string one, string two, string three) {
            labelColumn1.LabelProp = one;
            labelColumn2.LabelProp = two;
            labelColumn3.LabelProp = three;

        }
    }
}
