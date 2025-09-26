using System.Runtime.Serialization;

namespace Common
{
    [DataContract]
    public class ValidationFault
    {
        [DataMember] public string Reason { get; set; }
    }
}
