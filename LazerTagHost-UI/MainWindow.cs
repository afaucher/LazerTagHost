using System;
using Gtk;
using LazerTagHostLibrary;
using System.Collections.Generic;
using LazerTagHostUI;

public partial class MainWindow : Gtk.Window
{
    private HostGun hg = null;
    private HostWindow hw = null;

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



    protected void StartGameType (object sender, System.EventArgs e)
    {
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




        hw.Show();
    }


    

    
    



}
