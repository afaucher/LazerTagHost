
using System;

namespace LazerTagHostUI
{


    [System.ComponentModel.ToolboxItem(true)]
    public partial class PlayerSelectionScreen : Gtk.Bin
    {
        HostControlListener listener = null;

        public PlayerSelectionScreen ()
        {
            this.Build ();

            playerselector.SetListener(SelectionChanged);
        }

        void SelectionChanged(uint team_index, uint player_index) {
            if (listener != null) {
                listener.SelectionChanged(team_index, player_index);
            }
        }


        public PlayerSelector GetPlayerSelector() {
            return playerselector;
        }

        public void SetControlOptions(bool drop,
                                      bool rename,
                                      bool late,
                                      bool abort,
                                      bool next,
                                      bool pause)
        {
            buttonDropPlayer.Sensitive = drop;
            comboboxentryRenamePlayer.Sensitive = rename;
            buttonLateJoin.Sensitive = late;
            buttonAbortHost.Sensitive = abort;
            buttonNext.Sensitive = next;
            buttonPause.Sensitive = pause;
        }

        public void SetTitle(string title) {
            labelTitle.LabelProp = title;
        }

        public interface HostControlListener
        {
            void DropPlayer(object sender, System.EventArgs e);
            void RenamePlayer(object sender, System.EventArgs e);
            void LateJoin(object sender, System.EventArgs e);
            void Abort(object sender, System.EventArgs e);
            void Pause(object sender, System.EventArgs e);
            void Next(object sender, System.EventArgs e);
            void SelectionChanged(uint team_index, uint player_index);
            void RelativeScoresToggle(bool show_relative);
        }

        public void SubscribeEvents(HostControlListener listener)
        {
            this.listener = listener;
            buttonDropPlayer.Clicked += new System.EventHandler(listener.DropPlayer);
            comboboxentryRenamePlayer.Changed += new System.EventHandler(listener.RenamePlayer);
            buttonLateJoin.Clicked += new System.EventHandler(listener.LateJoin);
            buttonAbortHost.Clicked += new System.EventHandler(listener.Abort);
            buttonPause.Clicked += new System.EventHandler(listener.Pause);
            buttonNext.Clicked += new System.EventHandler(listener.Next);

        }

        protected virtual void RelativeScoresToggled (object sender, System.EventArgs e)
        {
            if (listener == null) return;

            listener.RelativeScoresToggle(togglebuttonRelativeScores.Active);
        }
        
        


    }
}
