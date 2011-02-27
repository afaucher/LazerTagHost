using System;
using System.Diagnostics;

namespace LazerTagHostLibrary
{
    public class PacketPacker
    {
        public PacketPacker ()
        {
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

        public static UInt16[] packHostedTag(int team, int player, int dmg)
        {
            Debug.Assert(team >= 0 && team <= 3);
            Debug.Assert(player >= 0 && player <= 7);
            Debug.Assert(dmg >= 1 && dmg <= 4);
            //TH TL PH PM PL X1 X2
            byte flags =
                (byte)((team << 5)
                    | (player << 2)
                    | dmg);
            UInt16[] values = new UInt16[]{
                flags
            };
            return values;
        }

        public static UInt16[] packUnhostedTag(int dmg)
        {
            //0 0 0 0 0 X1 X2
            Debug.Assert(dmg >= 1 && dmg <= 4);
            UInt16[] values = new UInt16[]{
                (byte)dmg,
            };
            return values;
        }

        public static UInt16[] packZoneBeacon()
        {
            //TODO
            //TH TL HF X2 X1
            // Zones
            //      0  0  1 : Reserved
            //      0  1  0 : Area under contention
            //      0  1  1 : Team base
            Debug.Assert(false);
            return null;
        }

        public static UInt16[] packPlayerBeacon(
            int team,
            bool hit_flag,
            int hit_count)
        {
            //TH TL HF X2 X1
            // Beacon
            //      0  0  0 : No hit
            //      1  0  0 : 1 dmg
            //      1  0  1 : 2 dmg
            //      1  1  0 : 3 dmg
            //      1  1  1 : 4 dmg
            byte flags =
                (byte)((team & 0x3) << 3
                    | (hit_flag ? 1 : 0) << 2
                    | (hit_count & 0x3));
            UInt16[] values = new UInt16[]{
                flags
            };
            return values;
        }

        //Entirely untested
        public static UInt16[] packGameDefinition(
            HostGun.CommandCode game_type,
            UInt16 game_id,
            int game_time_minutes,
            int tags,
            int reloads,
            int sheild,
            int mega,

            bool extended_tagging,
            bool unlimited_ammo,
            bool unlimited_mega,
            bool friendly_fire,
            bool medic_mode,
            bool rapid_tags,
            bool hunters_hunted,
            bool hunters_hunted_direction,

            bool zones,
            bool bases_are_teams,
            bool tagged_players_are_disabled,
            bool base_areas_revive_players,
            bool base_areas_are_hospitals,
            bool base_areas_fire_at_players,
            int number_of_teams_in_game,
            char[] game_type_name)
        {
            //assert(game_type_name.Length == 4);
            Debug.Assert(number_of_teams_in_game >= 0 && number_of_teams_in_game <= 3);
            byte flags =
                (byte)((extended_tagging ? 1 : 0) << 7
                    | (unlimited_ammo ? 1 : 1) << 6
                    | (unlimited_mega ? 1 : 1) << 5
                    | (friendly_fire ? 1 : 0) << 4
                    | (medic_mode ? 1 : 0) << 3
                    | (rapid_tags ? 1 : 0) << 2
                    | (hunters_hunted ? 1 : 0) << 1
                    | (hunters_hunted_direction ? 1 : 0) << 0);
            byte flags2 =
                (byte)((zones ? 1 : 0) << 7
                    | (bases_are_teams ? 1 : 0) << 6
                    | (tagged_players_are_disabled ? 1 : 0) << 5
                    | (base_areas_revive_players ? 1 : 0) << 4
                    | (base_areas_are_hospitals ? 1 : 0) << 3
                    | (base_areas_fire_at_players ? 1 : 0) << 2
                    | (number_of_teams_in_game & 0x03));
            String flags_string = String.Format("{0:x}{1:x}",flags,flags2);
            Console.WriteLine(flags_string);
            /*flags = 0x78;
            flags2 = 0xA3;
            flags_string = String.Format("{0:x}{1:x}",flags,flags2);
            Console.WriteLine(flags_string);*/

            UInt16[] values = new UInt16[] {
                (UInt16)game_type,
                game_id,//Game ID
                DecimalToDecimalHex((byte)game_time_minutes), //game time minutes
                DecimalToDecimalHex((byte)tags), //tags
                DecimalToDecimalHex((byte)reloads), //reloads
                DecimalToDecimalHex((byte)sheild), //sheild
                DecimalToDecimalHex((byte)mega), //mega
                flags,
                flags2,
            };
            if (game_type_name != null && game_type_name.Length > 0) {
                UInt16[] values2 = new UInt16[values.Length + game_type_name.Length];

                values.CopyTo(values2,0);
                game_type_name.CopyTo(values2,values.Length);
            }

            return values;
        }

        //Entirely untested
        public static UInt16[] packTextMessage(String message) {
            //No idea what the maximum length is, 5 works
            Debug.Assert(message.Length <= 5);
            UInt16[] values = new UInt16[message.Length + 1];
            //values[0] = (UInt16)HostGun.CommandCode.COMMAND_CODE_TEXT_MESSAGE;
            int i = 0;
            char[] source = message.ToCharArray();
            for (i = 0; i < source.Length; i++) {
                values[i+1] = source[i];
            }
            return values;
        }
    }
}

