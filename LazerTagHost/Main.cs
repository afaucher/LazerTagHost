using System;
using LazerTagHostLibrary;

namespace LazerTagHost
{

    

    
    class MainClass
    {

        
        public static void Main (string[] args)
        {
            if (args.Length == 0) {
                Console.WriteLine("Format: LaserTagHost.exe <serial port>");
                return;
            }
            
            HostGun hg = new HostGun(args[0], null);
            hg.autostart = true;
            
            //hg.RunRankTests();
            
            while (true) {
                
                hg.Update();
                
            }
        }
    }
}
