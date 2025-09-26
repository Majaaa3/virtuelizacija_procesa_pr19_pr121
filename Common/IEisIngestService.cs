using System.ServiceModel;
using System.Runtime.Serialization;
using Common;

namespace Common
{
    [ServiceContract]
    public interface IEisIngestService
    {
        [OperationContract]
        [FaultContract(typeof(DataFormatFault))]
        [FaultContract(typeof(ValidationFault))]
        Ack StartSession(EisMeta meta);

        [OperationContract]
        [FaultContract(typeof(DataFormatFault))]
        [FaultContract(typeof(ValidationFault))]
        Ack PushSample(string sessionId, EisSample sample);

        [OperationContract]
        [FaultContract(typeof(DataFormatFault))]
        [FaultContract(typeof(ValidationFault))]
        Ack EndSession(string sessionId);
    }
}
