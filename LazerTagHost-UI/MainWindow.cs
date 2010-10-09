using System;
using Gtk;
using LazerTagHostLibrary;
using System.Collections.Generic;

public partial class MainWindow : Gtk.Window, HostChangedListener
{
    private HostGun hg = null;
    private Gtk.RadioButton[,] radiobuttonPlayers = new Gtk.RadioButton[3,8];

    public MainWindow () : base(Gtk.WindowType.Toplevel)
    {
        Build ();

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
                rb.Label = "      Open      ";

                this.table2.Add(rb);
                Gtk.Table.TableChild tc = ((Gtk.Table.TableChild)(this.table2[rb]));
                tc.TopAttach = player_index + 1;
                tc.BottomAttach = player_index + 2;
                tc.LeftAttach = team_index + 1;
                tc.RightAttach = team_index + 2;

            }
        }

        List<string> ports = LazerTagSerial.GetSerialPorts();

        foreach (string port in ports) {
            this.comboboxentryArduinoPorts.AppendText(port);
        }

        hg = new HostGun(null, this);

        foreach (string port in ports) {
            if (hg.SetDevice(port)) {
                comboboxentryArduinoPorts.Entry.Text = port;
                buttonStartHost.Sensitive = true;
                SetTranscieverStatusImage("gtk-apply");
                break;
            }
        }

        this.ShowAll();


    }

    private void SetTranscieverStatusImage(string gtk_name)
    {
        imageTransceiverStatus.Pixbuf = Stetic.IconLoader.LoadIcon(this, gtk_name, Gtk.IconSize.Menu, 16);
    }

    protected virtual void TransceiverChanged (object sender, System.EventArgs e)
    {
        if (hg.SetDevice(comboboxentryArduinoPorts.ActiveText)) {
            buttonStartHost.Sensitive = true;
            SetTranscieverStatusImage("gtk-apply");
        } else {
            buttonStartHost.Sensitive = false;
            SetTranscieverStatusImage("gtk-dialog-error");
        }
    }

    protected void OnDeleteEvent (object sender, DeleteEventArgs a)
    {
        Application.Quit ();
        a.RetVal = true;
    }

     private bool HostUpdate()
    {
        hg.Update();
        labelCountdown.Text = hg.GetCountdown();
        return true;
    }

    protected virtual void RenamePlayer (object sender, System.EventArgs e)
    {
        string name = comboboxentryName.Entry.Text;
        Console.WriteLine("Name: " + name + ", " + comboboxentryName.ActiveText);
        //comboboxentryName.GetA

        if (name.Length < 3) return;

        comboboxentryName.AppendText(name);

        bool found = false;
        for (uint team_index = 0; team_index < 3; team_index++) {
            for (uint player_index = 0; player_index < 8; player_index++) {
                Gtk.RadioButton rb = radiobuttonPlayers[team_index,player_index];
                if (!rb.Active) continue;

                hg.SetPlayerName((int)team_index, (int)player_index, name);
                found = true;

                break;
            }
        }

        if (found) {
            RefreshPlayerList();
        }
    }

    private void RefreshPlayerList()
    {
        for (uint team_index = 0; team_index < 3; team_index++) {
            for (uint player_index = 0; player_index < 8; player_index++) {
                Gtk.RadioButton rb = this.radiobuttonPlayers[team_index,player_index];
                bool found = false;
                Player found_player = hg.LookupPlayer((int)team_index + 1, (int)player_index);

                Console.WriteLine("Set " + team_index + "," + player_index + " to " + (found ? "found" : "not found"));
                if (found_player != null) {

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
                        break;
                    }
                    rb.Label = text;

                } else {
                    rb.Label = "      Open      ";
                }
            }
        }
    }

    protected virtual void DropPlayer (object sender, System.EventArgs e)
    {
        for (uint team_index = 0; team_index < 3; team_index++) {
            for (uint player_index = 0; player_index < 8; player_index++) {
                Gtk.RadioButton rb = radiobuttonPlayers[team_index,player_index];
                if (!rb.Active) continue;

                hg.DropPlayer((int)team_index, (int)player_index);

                return;
            }
        }
    }

#region HostChangedListener implementation
    void HostChangedListener.PlayerListChanged(List<Player> players) {
        RefreshPlayerList();
    }

    void HostChangedListener.GameStateChanged(HostGun.HostingState state) {
        RefreshPlayerList();
    }

#endregion

    protected void StartGameType (object sender, System.EventArgs e)
    {
        this.notebookMain.CurrentPage = 1;
        hg.Init2TeamHostMode(
                             (byte)this.spinbuttonGameTime.ValueAsInt,
                             (byte)this.spinbuttonTags.ValueAsInt,
                             (byte)0xff,// (byte)this.spinbuttonReloads.Value,
                             (byte)this.spinbuttonSheild.ValueAsInt,
                             (byte)this.spinbuttonMega.ValueAsInt,
                             this.checkbuttonFriendlyFire.Active,
                             this.checkbuttonMedicMode.Active);
        //hg.Init2TeamHostMode(1,10,0xff,15,10,true,false);

        hg.StartServer();
        GLib.TimeoutHandler th = new GLib.TimeoutHandler(HostUpdate);
        GLib.Timeout.Add(100,th);
        labelSetup.Sensitive = false;
        labelJoin.Sensitive = true;
        buttonStartHost.Sensitive = false;
    }
    
    protected virtual void DelayGame (object sender, System.EventArgs e)
    {
        hg.DelayGame(60);
    }
    
    protected virtual void CancelGame (object sender, System.EventArgs e)
    {
        hg.EndGame();
        notebookMain.CurrentPage = 0;
        labelSetup.Sensitive = true;
        labelJoin.Sensitive = false;
    }
    

    
    



}
