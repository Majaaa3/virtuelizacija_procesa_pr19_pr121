using System;
using System.Runtime.Serialization;

namespace Common
{
    [DataContract]
    public class EisSample
    {
        [DataMember] public int RowIndex { get; set; }
        [DataMember] public double FrequencyHz { get; set; }
        [DataMember] public double R_ohm { get; set; }
        [DataMember] public double X_ohm { get; set; }
        [DataMember] public double T_degC { get; set; }
        [DataMember] public double Range_ohm { get; set; }
        [DataMember] public DateTime TimestampLocal { get; set; }
    }
}
