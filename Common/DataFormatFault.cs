using System.Runtime.Serialization;

namespace Common
{
    
    [DataContract]
    public class DataFormatFault
    {
        [DataMember] public string Reason { get; set; }
    }
}
