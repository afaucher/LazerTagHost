using System;
using Gtk;
using LazerTagHostLibrary;
using System.Collections.Generic;
using LazerTagHostUI;

public partial class MainWindow : Gtk.Window
{
    private HostGun hg = null;
    private HostWindow hw = null;
    HostGun.CommandCode game_type = HostGun.CommandCode.COMMAND_CODE_CUSTOM_GAME_MODE_HOST;

    public MainWindow () : base(Gtk.WindowType.Toplevel)
    {
        Build ();

        List<string> ports = LazerTagSerial.GetSerialPorts();

        foreach (string port in ports) {
            this.comboboxentryArduinoPorts.AppendText(port);
        }

        hg = new HostGun(null, null);

        foreach (string port in ports) {
            if (hg.SetDevice(port)) {
                comboboxentryArduinoPorts.Entry.Text = port;
                buttonStartHost.Sensitive = true;
                SetTranscieverStatusImage("gtk-apply");
                break;
            }
        }

        this.ShowAll();

        hw = new HostWindow(hg);
        hw.Modal = true;
        hw.Hide();

        UpdateGameType();
    }

    private void SetTranscieverStatusImage(string gtk_name)
    {
        imageTransceiverStatus.Pixbuf = Stetic.IconLoader.LoadIcon(this, gtk_name, Gtk.IconSize.Menu);
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

    private byte ConvertGameValue(int input)
    {
        if (input >= 100 || input < 0) return 0xff;
        return (byte)input;
    }

    protected void StartGameType (object sender, System.EventArgs e)
    {
        hg.DynamicHostMode(game_type,
                        ConvertGameValue(spinbuttonGameTime.ValueAsInt),
                        ConvertGameValue(spinbuttonTags.ValueAsInt),
                        ConvertGameValue(spinbuttonReloads.ValueAsInt),
                        ConvertGameValue(spinbuttonSheild.ValueAsInt),
                        ConvertGameValue(spinbuttonMega.ValueAsInt),
                        this.checkbuttonFriendlyFire.Active,
                        this.checkbuttonMedicMode.Active,
                        ConvertGameValue(spinbuttonNumberOfTeams.ValueAsInt));

        hg.SetGameStartCountdownTime(spinbuttonCountdownTime.ValueAsInt);
        hg.StartServer();
        hw.Show();
    }

    private void SetGameDefaults(int time, int reloads, int mega, int sheilds, int tags, bool ff, bool medic, int teams, bool medic_enabled, int time_step)
    {
        spinbuttonGameTime.Value = time;
        if (time_step == 2) {
            spinbuttonGameTime.Adjustment.Upper = 98;
            spinbuttonGameTime.Adjustment.Lower = 2;
        } else if (time_step == 1) {
            spinbuttonGameTime.Adjustment.Upper = 99;
            spinbuttonGameTime.Adjustment.Lower = 1;
        }
        spinbuttonGameTime.ClimbRate = time_step;
        spinbuttonGameTime.Adjustment.StepIncrement = time_step;
        spinbuttonReloads.Value = reloads;
        spinbuttonMega.Value = mega;
        spinbuttonSheild.Value = sheilds;
        spinbuttonTags.Value = tags;
        checkbuttonFriendlyFire.Active = ff;
        if (teams <= 1) {
            checkbuttonFriendlyFire.Sensitive = false;
            checkbuttonFriendlyFire.Active = false;
        } else {
            checkbuttonFriendlyFire.Sensitive = true;
        }
        checkbuttonMedicMode.Active = medic;
        if (teams <= 1 || !medic_enabled) {
            checkbuttonMedicMode.Sensitive = false;
            checkbuttonMedicMode.Active = false;
        } else {
            checkbuttonMedicMode.Sensitive = true;
        }
        spinbuttonNumberOfTeams.Value = teams;
    }

    private void UpdateGameType()
    {
        switch (comboboxGameType.Active) {
        case 0:
            //Custom Laser Tag (Solo)
            game_type = HostGun.CommandCode.COMMAND_CODE_CUSTOM_GAME_MODE_HOST;
            SetGameDefaults(10,100,10,15,10,false,false,1,true,1);
            break;
        case 1:
            //Own The Zone (Solo)
            game_type = HostGun.CommandCode.COMMAND_CODE_OWN_THE_ZONE_GAME_MODE_HOST;
            SetGameDefaults(10,15,0,45,10,false,false,1,false,1);
            break;
        case 2:
            //2-Team Customized Lazer Tag
            game_type = HostGun.CommandCode.COMMAND_CODE_2TMS_GAME_MODE_HOST;
            SetGameDefaults(15,100,10,15,20,true,true,2,true,1);
            break;
        case 3:
            //3-Team Customized Lazer Tag
            game_type = HostGun.CommandCode.COMMAND_CODE_3TMS_GAME_MODE_HOST;
            SetGameDefaults(15,100,10,15,20,true,true,3,true,1);
            break;
        case 4:
            //Hide And Seek
            game_type = HostGun.CommandCode.COMMAND_CODE_HIDE_AND_SEEK_GAME_MODE_HOST;
            SetGameDefaults(10,5,15,30,25,true,true,2,true,2);
            break;
        case 5:
            //Hunt The Prey
            game_type = HostGun.CommandCode.COMMAND_CODE_HUNT_THE_PREY_GAME_MODE_HOST;
            SetGameDefaults(10,5,15,30,25,true,true,3,true,1);
            break;
        case 6:
            //2-Team Kings
            game_type = HostGun.CommandCode.COMMAND_CODE_2_KINGS_GAME_MODE_HOST;
            SetGameDefaults(15,20,0,30,15,true,true,2,true,1);
            break;
        case 7:
            //3-Team Kings
            game_type = HostGun.CommandCode.COMMAND_CODE_3_KINGS_GAME_MODE_HOST;
            SetGameDefaults(30,20,0,30,15,true,true,3,true,1);
            break;
        case 8:
            //2-Team Own The Zone
            game_type = HostGun.CommandCode.COMMAND_CODE_2TMS_OWN_THE_ZONE_GAME_MODE_HOST;
            SetGameDefaults(15,15,0,45,10,true,false,2,false,1);
            break;
        case 9:
            //3-Team Own The Zone
            game_type = HostGun.CommandCode.COMMAND_CODE_3TMS_OWN_THE_ZONE_GAME_MODE_HOST;
            SetGameDefaults(20,15,0,45,10,true,false,3,false,1);
            break;
        }
    }

    protected virtual void GameTypeChanged (object sender, System.EventArgs e)
    {

        UpdateGameType();
    }
    
    

    

    
    



}
