using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using MJIoT_DBModel;
using Newtonsoft.Json;

namespace MJIoT_EventsFunction
{
    //class Program
    //{
    //    static void Main(string[] args)
    //    {
    //    }
    //}

    public class DeviceEventsHandler
    {
        public static DeviceToCloudMessage DeserializeMessage(string eventHubMessage)
        {
            return JsonConvert.DeserializeObject<DeviceToCloudMessage>(eventHubMessage as string);
        }

        public static void SaveValue(DeviceToCloudMessage message)
        {
            using (var context = new MJIoTDBContext())
            {
                var deviceType = context.Devices.Include("DeviceType")
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
            }
        }
    }
}
