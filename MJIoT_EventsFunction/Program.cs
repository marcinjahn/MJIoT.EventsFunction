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
        static string connectionString = "HostName=MJIoT-IoTHub.azure-devices.net;SharedAccessKeyName=iothubowner;SharedAccessKey=ieEi5UNBx6C7js/+e6/G+oM3K/isI4WRARv2bBNd270=";


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
                    SendCloudToDeviceMessageAsync(listener.Id.ToString(), message.Value).Wait();
                }
            }
        }
    }
}
