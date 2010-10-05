
using System;

namespace LazerTagHostLibrary
{


    public class IRPacket {
        public enum PacketType {
            PACKET_TYPE_LTX,
            PACKET_TYPE_LTTO,
        };
        
        public PacketType packet_type;
        public UInt16 data;
        public UInt16 number_of_bits;
        
        public IRPacket(
                        PacketType packet_type,
            UInt16 data,
            UInt16 number_of_bits)
        {
            this.packet_type = packet_type;
            this.data = data;
            this.number_of_bits = number_of_bits;
        }
    }
}
