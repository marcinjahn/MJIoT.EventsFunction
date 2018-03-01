using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using MJIoT_DBModel;
using Newtonsoft.Json;

using System.Data.Entity;

using Microsoft.Azure.Devices;


namespace MJIoT_EventsFunction
{
    class Program
    {
        static void Main(string[] args)
        {

            Console.WriteLine("Events Handler");

            string newMessage = @"{DeviceId: ""7"",
                                PropertyName: ""SimulatedSwitchState"",
                                Value: ""false""
                                }";

            var message = new DeviceToCloudMessage(newMessage);
            var handler = new EventHandler(message);
            handler.HandleMessage().Wait();
           
        }
    }

    public class EventHandler
    {
        private IoTHubService IoTHubService { get; set; }
        private MJIoTDb ModelDb { get; set; }
        private DeviceToCloudMessage Message { get; set; }

        public EventHandler(DeviceToCloudMessage message)
        {
            IoTHubService = new IoTHubService();
            ModelDb = new MJIoTDb();
            Message = message;
        }

        public async Task HandleMessage()
        {
            ModelDb.SaveValue(Message);

            var isSenderProperty = ModelDb.IsItSenderProperty(Message.DeviceId, Message.PropertyName);
            if (isSenderProperty)
                await NotifyListeners();

        }

        private async Task NotifyListeners()
        {
            var listeners = ModelDb.GetListeners(Message.DeviceId);
            var notifyTasks = new List<Task>();
            foreach (var listener in listeners)
                notifyTasks.Add(NotifyListener(listener.Id));
            await Task.WhenAll(notifyTasks);
        }

        private async Task NotifyListener(int listenerId)
        {
            var deviceType = ModelDb.GetDeviceType(listenerId);

            if (await ShouldMessageBeSent(deviceType, listenerId))
            {
                var message = GetMessageToSend(listenerId.ToString(), deviceType);
                await IoTHubService.SendToListenerAsync(message);
            }
        }

        private async Task<bool> ShouldMessageBeSent(DeviceType deviceType, int listenerId)
        {
            //OFFLINE MESSAGING ENABLED?
            if (!ModelDb.IsOfflineMessagingEnabled(deviceType))
            {
                //IS DEVICE ONLINE?
                if (!(await IoTHubService.IsDeviceOnline(listenerId.ToString())))
                    return false;
            }

            return true;
        }

        private IoTHubMessage GetMessageToSend(string deviceId, DeviceType deviceType)
        {
            var listenerPropertyType = ModelDb.GetListenerPropertyType(deviceType);
            var format = listenerPropertyType.Format;
            var convertedValue =  MessageConverter.Convert(Message.Value, format);

            return new IoTHubMessage(deviceId, listenerPropertyType.Name, convertedValue);
        }
    }

    public class IoTHubMessage
    {
        public IoTHubMessage(string receiverId, string propertyName,  string value)
        {
            ReceiverId = receiverId;
            PropertyName = propertyName;
            Value = value;
        }

        public string ReceiverId { get; set; }
        public string PropertyName { get; set; }
        public string Value { get; set; }
    }


    public class MJIoTDb
    {
        public MJIoTDBContext Context { get; set; }

        public MJIoTDb()
        {
            Context = new MJIoTDBContext();
        }

        public void SaveValue(DeviceToCloudMessage message)
        {
            DeviceProperty deviceProperty = GetDeviceProperty(message);
            deviceProperty.Value = message.Value;
            Context.SaveChanges();
        }

        public List<MJIoT_DBModel.Device> GetListeners(int senderId)
        {
            var listeners = Context.Devices.Include("ListenerDevices")
                    .Where(n => n.Id == senderId)
                    .Select(n => n.ListenerDevices)
                    .FirstOrDefault();

            return listeners.ToList();
        }

        public bool IsItSenderProperty(int deviceId, string property)
        {
            var deviceType = GetDeviceType(deviceId);
            if (deviceType.SenderProperty == null)
                return false;
            else
                return deviceType.SenderProperty.Name == property;
        }

        public PropertyType GetListenerPropertyType(DeviceType deviceType) //NEEDS TO BE TESTED
        {
            var i = 0;
            while (true && i <= 100)  //DANGEROUS - MIGHT BE INIFNITE (for now <= 100 is a workaround)
            {
                i++;
                var property = deviceType.ListenerProperty;

                if (property != null)
                    return property;
                else
                {
                    var baseType = deviceType.BaseDeviceType;

                    if (baseType != null)
                        deviceType = baseType;
                }
            }

            throw new Exception("GetListenerPropertyType didn't find the Property Type!!");
        }

        public bool IsOfflineMessagingEnabled(DeviceType deviceType)
        {
            return deviceType.OfflineMessagesEnabled;
        }

        private DeviceProperty GetDeviceProperty(DeviceToCloudMessage message)
        {
            DeviceType deviceType = GetDeviceType(message.DeviceId);
            PropertyType propertyType = GetPropertyType(message.PropertyName, deviceType.Id);
            return GetDeviceProperty(message.DeviceId, propertyType.Id);
        }

        private DeviceProperty GetDeviceProperty(int deviceId, int propertyTypeId)
        {
            var deviceProperty = Context.DeviceProperties
                            .Include("PropertyType").Include("Device")
                            .Where(n => n.Device.Id == deviceId && n.PropertyType.Id == propertyTypeId)
                            .FirstOrDefault();

            if (deviceProperty == null)
            {
                throw new NullReferenceException("Property (" + propertyTypeId.ToString() + ") of device (" + deviceId + ") does not exist. Procedure aborted.");
            }

            return deviceProperty;
        }

        private PropertyType GetPropertyType(string propertyName, int deviceTypeId)
        {
            var propertyType = Context.PropertyTypes
                            .Include("DeviceType")
                            .Where(n => n.Name == propertyName && n.DeviceType.Id == deviceTypeId)
                            .FirstOrDefault();
            if (propertyType == null)
            {
                throw new NullReferenceException("Property type (" + propertyName + ") does not exist. Procedure aborted.");
            }

            return propertyType;
        }

        public DeviceType GetDeviceType(int deviceId)
        {

            //var deviceType = Context.Devices.Include("DeviceType").Include("DeviceType.SenderProperty").Include("DeviceType.ListenerProperty")
            //        .Where(n => n.Id == deviceId)
            //        .Select(n => n.DeviceType)
            //        .FirstOrDefault();


            //lambda in Include() requires using System.Data.Entity;
            var deviceType = Context.Devices.Include(n => n.DeviceType)
                   .Where(n => n.Id == deviceId)
                   .Select(n => n.DeviceType).Include(n => n.ListenerProperty).Include(n => n.SenderProperty)
                   .FirstOrDefault();

            if (deviceType == null)
            {
                throw new NullReferenceException("Device (" + deviceId + ") does not exist. Procedure aborted.");
            }

            return deviceType;
        }
    }


    public class IoTHubService
    {
        public ServiceClient ServiceClient { get; set; }
        public string ConnectionString { get; set; }

        public IoTHubService()
        {
            ConnectionString = "HostName=MJIoT-Hub.azure-devices.net;SharedAccessKeyName=iothubowner;SharedAccessKey=SzQKdF1y6bAEgGfZei2bmq1Jd83odc+B2x197n2MtxA=";
            ServiceClient = Microsoft.Azure.Devices.ServiceClient.CreateFromConnectionString(ConnectionString);
        }

        public async Task SendToListenerAsync(IoTHubMessage message)
        {
            var messageString = GenerateC2DMessage(message.PropertyName, message.Value);
            await SendC2DMessageAsync(message.ReceiverId, messageString);
        }

        public async Task<Boolean> IsDeviceOnline(string deviceId)
        {
            var methodInvocation = new CloudToDeviceMethod("conn") { ResponseTimeout = TimeSpan.FromSeconds(5) };
            CloudToDeviceMethodResult response;
            try
            {
                response = await ServiceClient.InvokeDeviceMethodAsync(deviceId, methodInvocation);
            }
            catch (Exception)
            {
                return false;
            }

            return true;
        }

        private async Task SendC2DMessageAsync(string deviceId, string message)
        {
            var messageObject = new Message(System.Text.Encoding.ASCII.GetBytes(message));
            await ServiceClient.SendAsync(deviceId, messageObject);
        }

        private string GenerateC2DMessage(string property, string value)
        {
            return @"{""PropertyName"":""" + property + @""",""Value"":""" + value + @"""}";
        }

    }

    public class DeviceToCloudMessage
    {
        public DeviceToCloudMessage()
        {

        }

        public DeviceToCloudMessage(string message)
        {
            var msg = JsonConvert.DeserializeObject<DeviceToCloudMessage>(message as string);
            DeviceId = msg.DeviceId;
            PropertyName = msg.PropertyName;
            Value = msg.Value;
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




    public class MessageConverter
    {
        public static string Convert(string value, PropertyTypeFormats targetType)
        {
            var stringValue = value;
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
        }
    }
}
