using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using MJIoT_DBModel;
using Newtonsoft.Json;

using Microsoft.Azure.Devices;


namespace MJIoT_EventsFunction
{
    //class Program
    //{
    //    static void Main(string[] args)
    //    {
    //    Log = log;  //now Log is globally available
    //log.Info($"C# Event Hub trigger function processed a message: {myEventHubMessage}");

    //// var message = DeviceEventsHandler.DeserializeMessage(myEventHubMessage);

    //var message = JsonConvert.DeserializeObject<DeviceToCloudMessage>(myEventHubMessage as string);
    //var isSenderProperty = DeviceEventsHandler.SaveValue(message);

    //log.Info(isSenderProperty.ToString());

    //if (isSenderProperty)
    //    DeviceEventsHandler.SendMessageToListener(message);
    //    }
    //}

    public class DeviceEventsHandler
    {
        static ServiceClient serviceClient;
        static string connectionString = "HostName=MJIoT-Hub.azure-devices.net;SharedAccessKeyName=iothubowner;SharedAccessKey=SzQKdF1y6bAEgGfZei2bmq1Jd83odc+B2x197n2MtxA=";


        public static DeviceToCloudMessage DeserializeMessage(string eventHubMessage)
        {
            return JsonConvert.DeserializeObject<DeviceToCloudMessage>(eventHubMessage as string);
        }

        private async static Task SendCloudToDeviceMessageAsync(string deviceId, string message)
        {
            var commandMessage = new Message(System.Text.Encoding.ASCII.GetBytes(message));
            await serviceClient.SendAsync(deviceId, commandMessage);
        }

        public static bool SaveValue(DeviceToCloudMessage message)
        {
            using (var context = new MJIoTDBContext())
            {
                var deviceType = context.Devices.Include("DeviceType").Include("SenderProperty")
                    .Where(n => n.Id == message.DeviceId)
                    .Select(n => n.DeviceType)
                    .FirstOrDefault();
                if (deviceType == null)
                {
                    throw new NullReferenceException("Device (" + message.DeviceId + ") does not exist. Procedure aborted.");
                }

                var propertyType = context.PropertyTypes
                    .Include("DeviceType")
                    .Where(n => n.Name == message.PropertyName && n.DeviceType.Id == deviceType.Id)
                    .FirstOrDefault();
                if (propertyType == null)
                {
                    throw new NullReferenceException("Property type (" + message.PropertyName + ") does not exist. Procedure aborted.");
                }


                var prop = context.DeviceProperties
                    .Include("PropertyType").Include("Device")
                    .Where(n => n.Device.Id == message.DeviceId && n.PropertyType.Id == propertyType.Id)
                    .FirstOrDefault();

                if (prop == null)
                {
                    throw new NullReferenceException("Property (" + message.PropertyName + ") of device (" + message.DeviceId + ") does not exist. Procedure aborted.");
                }

                prop.Value = message.Value;

                context.SaveChanges();
                //  Log.Info(deviceType.SenderProperty.Name);
                //Log.Info(message.PropertyName);

                if (deviceType.SenderProperty == null)
                    return false;
                return deviceType.SenderProperty.Name == message.PropertyName;
            }
        }

        public static void SendMessageToListener(DeviceToCloudMessage message)
        {
            serviceClient = ServiceClient.CreateFromConnectionString(connectionString);

            using (var context = new MJIoTDBContext())
            {
                var listeners = context.Devices.Include("ListenerDevices")
                    .Where(n => n.Id == message.DeviceId)
                    .Select(n => n.ListenerDevices)
                    .FirstOrDefault();


                foreach (var listener in listeners)
                {
                    //Log.Info(listener.Id.ToString());
                    var typeId = context.Devices.Include("DeviceType")
                    .Where(n => n.Id == listener.Id)
                    .Select(n => n.DeviceType)
                    .FirstOrDefault()
                    .Id;
                    var propertyName = GetListenerPropertyName(typeId);
                    var format = GetListenerPropertyType(typeId, propertyName);
                    var convertedValue = MessageConverter.Convert(message.Value, format);
                   // Log.Info(propertyName);
                    var msg = GenerateCloudToDeviceMessage(propertyName, convertedValue);
                    //Log.Info(convertedValue);
                    //Log.Info(msg);
                    SendCloudToDeviceMessageAsync(listener.Id.ToString(), msg).Wait();
                }
            }
        }

        public static PropertyTypeFormats GetListenerPropertyType(int typeId, string propertyName)
        {
            using (var context = new MJIoTDBContext())
            {
                PropertyTypeFormats? format = (PropertyTypeFormats?)context.PropertyTypes
                .Include("DeviceType").Include("Format")
                .Where(n => n.DeviceType.Id == typeId && n.Name == propertyName)
                .Select(n => n.Format)
                .FirstOrDefault();

                if (format != null)
                {
                    return format.Value;
                }
                else
                {
                    throw new Exception("GetListenerPropertyType had an error - returned format is null");
                }
            }
        }

        //INFINITE LOOP POSSIBILITY
        public static string GetListenerPropertyName(int deviceTypeId)
        {
            while (true)
            {
                using (var context = new MJIoTDBContext())
                {
                    var property = context.DeviceTypes
                    .Include("ListenerProperty")
                    .Where(n => n.Id == deviceTypeId)
                    .Select(n => n.ListenerProperty)
                    .FirstOrDefault();

                    if (property != null)
                        return property.Name;
                    else
                    {
                        var baseType = context.DeviceTypes
                        .Include("BaseDeviceType")
                        .Where(n => n.Id == deviceTypeId)
                        .Select(n => n.BaseDeviceType)
                        .FirstOrDefault();

                        if (baseType != null)
                            deviceTypeId = baseType.Id;
                    }
                }
            }
        }

        public static string GenerateCloudToDeviceMessage(string propertyName, string value)
        {
            return @"{""PropertyName"":""" + propertyName + @""",""Value"":""" + value + @"""}";
        }
    }


    public class MessageConverter
    {
        public static string Convert(object value, PropertyTypeFormats targetType)
        {
            var stringValue = value.ToString();
            double floatValue;
            int intValue;

            //ONEBYTE
            if (targetType == PropertyTypeFormats.OneByte)
            {
                if (stringValue == "true")
                    return "255";
                else if (stringValue == "false")
                    return "0";
                else if (Double.TryParse(stringValue, out floatValue))
                {
                    intValue = (int)floatValue > 255 ? 255 : (int)floatValue;
                    return intValue.ToString();
                }
                else
                    return "0";
            }

            //BOOLEAN
            else if (targetType == PropertyTypeFormats.Boolean)
            {
                if (stringValue == "true" || stringValue.ToLower() == "on")
                    return "true";
                else if (stringValue == "false" || stringValue.ToLower() == "off")
                    return "false";
                else if (Double.TryParse(stringValue, out floatValue))
                {
                    if (floatValue > 0)
                        return "true";
                    else
                        return "false";
                }
                else
                    return "false";
            }

            //FLOAT
            else if (targetType == PropertyTypeFormats.Float)
            {
                if (stringValue == "true")
                    return "1";  //?? What value should it be?
                else if (stringValue == "false")
                    return "0";
                else if (Double.TryParse(stringValue, out floatValue))
                {
                    return floatValue.ToString();
                }
                else
                    return "0";
            }

            //RAW
            else
            {
                return stringValue;
            }


            // if (sourceType = PropertyTypeFormats.Boolean)
            // {
            //     if (targetType == PropertyTypeFormats.OneByte) {
            //         if (stringValue == "true")
            //             return "255";
            //         else
            //             return "0";
            //     }
            //     else if (targetType == PropertyTypeFormats.Raw) {
            //         return stringValue;
            //     }
            //     else if (targetType == PropertyTypeFormats.Float) {
            //         if (stringValue == "true")
            //             return "1";  //?? not always good
            //         else
            //             return "0";
            //     }
            //     else {
            //         return stringValue;
            //     }
            // }
            // else if (sourceType = PropertyTypeFormats.OneByte)
            // {
            //     var floatValue = Float.Parse(stringValue);

            //     if (targetType == PropertyTypeFormats.Boolean) {
            //         if (floatValue > 0)
            //             return "true";
            //         else
            //             return "false";
            //     }
            //     else if (targetType == PropertyTypeFormats.Raw) {
            //         return stringValue;
            //     }
            //     else if (targetType == PropertyTypeFormats.Float) {
            //         return stringValue;
            //     }
            //     else {
            //         return stringValue;
            //     }
            // }
            // else if (sourceType = PropertyTypeFormats.Raw)  //problematic - non-sense
            // {
            //     if (targetType == PropertyTypeFormats.Boolean) {
            //         if (stringValue == "true" || stringValue.ToLower() == "on")
            //             return "true";
            //         else
            //             return "false";
            //     }
            //     else if (targetType == PropertyTypeFormats.Raw) {
            //         return stringValue;
            //     }
            //     else if (targetType == PropertyTypeFormats.Float) {
            //         float floatValue;
            //         var isOk = Float.TryParse(stringValue, out floatValue);
            //         if (isOK)
            //             return floatValue.ToString();
            //         else
            //             return "0";
            //     }
            //     else {
            //         return stringValue;
            //     }
            // }
            // else if (sourceType = PropertyTypeFormats.Float)
            // {
            //     var floatValue = Float.Parse(stringValue);

            //     if (targetType == PropertyTypeFormats.Boolean) {
            //         if (floatValue > 0)
            //             return "true";
            //         else
            //             return "false";
            //     }
            //     else if (targetType == PropertyTypeFormats.Raw) {
            //         return stringValue;
            //     }
            //     else if (targetType == PropertyTypeFormats.OneByte) {
            //         var intValue = (int)floatValue;

            //         if (intValue > 255)
            //             return "255";
            //         else
            //             return intValue.ToString();
            //     }
            //     else {
            //         return stringValue;
            //     }
            // }
        }
    }



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


    //used for MessageConverter
    // public enum PropertyTypeFormats : int
    // {
    //     Boolean,  //przełącznik
    //     OneByte,  //np. PWM
    //     Raw,   //string - informuje, że: jest stringiem oraz, że dane urządzenie odbiera informacje "surowe", cyzli w tkaiej formie w jakiej zostało wysłane z nadawcy - dlatego, że każda propercja zapisuje dane jako string!
    //     Float  //zmiennoprzecinkowe
    // };

    // public class CloudToDeviceMessage
    // {
    //     public CloudToDeviceMessage()
    //     {
    //     }

    //     public CloudToDeviceMessage(dynamic data)
    //     {
    //         PropertyName = data.PropertyName;  //Is it really needed? Device probably will know what it should set or do
    //         Value = data.Value;
    //         //Timestamp = data.Timestamp;
    //     }

    //     public int DeviceId { get; set; }
    //     public string PropertyName { get; set; }
    //     public string Value { get; set; }
    //     //public string Timestamp { get; set; }
    // }
}
