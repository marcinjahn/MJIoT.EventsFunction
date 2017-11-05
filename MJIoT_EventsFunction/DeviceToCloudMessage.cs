namespace MJIoT_EventsFunction
{
    public class DeviceToCloudMessage
    {
        public DeviceToCloudMessage()
        {
        }
        public DeviceToCloudMessage(dynamic data)
        {
            DeviceId = data.DeviceId;
            PropertyName = data.PropertyName;
            Value = data.Value;
            //Timestamp = data.Timestamp;
        }

        public int DeviceId { get; set; }
        public string PropertyName { get; set; }
        public string Value { get; set; }
        //public string Timestamp { get; set; }
    }
}
