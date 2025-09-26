using System.Runtime.Serialization;

namespace Common
{
    [DataContract]
    public class EisMeta
    {
        [DataMember] public string BatteryId { get; set; }
        [DataMember] public string TestId { get; set; }
        [DataMember] public int SoCPercent { get; set; }
        [DataMember] public string FileName { get; set; }
        [DataMember] public int TotalRows { get; set; }
    }
}
