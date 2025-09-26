using System.Runtime.Serialization;
using Common;

namespace Common
{
    [DataContract]
    public class Ack
    {
        [DataMember] public bool Ok { get; set; }
        [DataMember] public string Message { get; set; }
        [DataMember] public TransferStatus Status { get; set; }
        [DataMember] public string SessionId { get; set; }
    }
}
