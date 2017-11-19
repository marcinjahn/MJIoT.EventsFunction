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

    public class CloudToDeviceMessage
    {
        public CloudToDeviceMessage()
        {
        }

        public CloudToDeviceMessage(dynamic data)
        {
            PropertyName = data.PropertyName;  //Is it really needed? Device probably will know what it should set or do
            Value = data.Value;
            //Timestamp = data.Timestamp;
        }

        public int DeviceId { get; set; }
        public string PropertyName { get; set; }
        public string Value { get; set; }
        //public string Timestamp { get; set; }
    }
}
