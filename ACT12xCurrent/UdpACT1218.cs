using Newtonsoft.Json;
using StackExchange.Redis;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace DataAcquisition
{
    class UdpACT1218 : UdpACT12x
    {
        private static readonly log4net.ILog log = log4net.LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);
        private DataGridView dataGridView;
        private Dictionary<int, CurrentVoltageChannel> channels;
        private UdpACT12xConfig config;
        private int rowIndex;
        private string errMsg;
        private const int NumberOfChannels = 8;
        private byte[] ipArray;
        private Dictionary<string, DataValue> dataBuffer;
        private int count;
        
        private IDatabase db;
        private string Tag;
        private int times;

        public UdpACT1218(UdpACT12xConfig config, DataGridView dataGridView,int rowIndex,Dictionary<string, DataValue> valueMap, ConnectionMultiplexer redis) : base(config.LocalPort,config.LocalIP, config.RemotePort, config.RemoteIpAddress,redis)
        {
            this.Tag = config.RemoteIpAddress + " : ";
            this.dataGridView = dataGridView;
            this.dataBuffer = valueMap;
            this.config = config;
            this.rowIndex = rowIndex;
            this.count = 0;
            this.times = 0;
            channels = new Dictionary<int, CurrentVoltageChannel>();
            //
            
            GetIpArray();
            LoadChannels();
            db = redis.GetDatabase();
        }

        void GetIpArray()
        {
            string[] parts = this.config.RemoteIpAddress.Split('.');
            ipArray = new byte[4];
            for (int i = 0; i < 4; i++)
            {
                ipArray[i] = Byte.Parse(parts[i]);
            }
        }

        private void LoadChannels()
        {
            using (SQLiteConnection connection = new SQLiteConnection(this.config.Database))
            {
                connection.Open();
                //string idHead = config.SensorId.Substring(0, config.SensorId.Length - 3);
                string strainStatement = "select SensorId,ChannelNo,InitValue,OutputRangeTop,OutputRangeBottom,MeasureRangeTop,MeasureRangeBottom,Type from CVChannels where GroupNo ='" + config.DeviceId + "'";
                SQLiteCommand command = new SQLiteCommand(strainStatement, connection);
                using (SQLiteDataReader reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        //string  groupId = reader.GetString(0);
                        string sensorId = reader.GetString(0);
                        int channelNo = reader.GetInt32(1);
                        double initValue = reader.GetDouble(2);
                        double outputRangeTop = reader.GetDouble(3);
                        double outputRangeBottom = reader.GetDouble(4);
                        double measureRangeTop = reader.GetDouble(5);
                        double measureRangeBottom = reader.GetDouble(6);
                        string type = reader.GetString(7);

                        CurrentVoltageChannel channel = new CurrentVoltageChannel(sensorId, channelNo, initValue, outputRangeTop, outputRangeBottom, measureRangeTop, measureRangeBottom,type);
                        
                        channels.Add(channelNo, channel);
                    }
                }
            }
        }

        private bool FrameCheck(byte[] buffer, int length)
        {
            if (length != 20)
            {
                return false;
            }
            return true;
            //if (buffer[0] == ipArray[0] && buffer[1] == ipArray[1] && buffer[2] == ipArray[2] && buffer[3] == ipArray[3])
            //{
            //    return true;
            //}
            //else
            //{
            //    return false;
            //}
        }

        private byte[] GetAcquisitionFrame()
        {

            byte[] frame = { 0x00, 0x10, 0x00, 0xc8, 0x00, 0x01, 0x01, 0x01 };
            return frame;
        }

        public override void Start()
        {
            base.Start();
            byte[] start = { 0x00, 0x10, 0x00, 0xc8, 0x00, 0x01, 0x01, 0x01 };
            //this.udpServer.Send(start,start.Length);
            
        }

        public override void Stop()
        {
            byte[] stop = { 0x00, 0x10, 0x00, 0xc8, 0x00, 0x01, 0x01, 0x00 };
            //this.udpServer.Send(stop, stop.Length);
            base.Stop();
            //redis.Close();
        }

        public override void ProcessData(byte[] buffer, int length)
        {

            bool checkPassed = this.FrameCheck(buffer, length);
            if (checkPassed == true)
            {
                //message = this.deviceId + "\r\n";
                int startIndex = 4;

                string message = "";

                //CurrentVoltageChannel cvc = new CurrentVoltageChannel("123", 1, 0, 20, 4, 20, 4);

                string stamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

                StringBuilder sb = new StringBuilder(1024);
                sb.Append(stamp + ",");

                for (int i = 0; i < NumberOfChannels; i++)
                {

                    byte[] bytes = new byte[2];
                    bytes[0] = buffer[startIndex + i * 2];
                    bytes[1] = buffer[startIndex + i * 2 + 1];                   

                    if (channels.ContainsKey(i+1))
                    {
                        CurrentVoltageChannel cvc = channels[i + 1];
                        double value = cvc.GetResult(bytes);
                        sb.Append(value.ToString() + ",");

                        string key = cvc.sensorId + "-" + cvc.type;
                        if (dataBuffer.ContainsKey(key))
                        {
                            dataBuffer[key].Value = Math.Round(value, 3) ;
                            //dataBuffer[key].Value = value;
                            //dataBuffer[key].Updated = true;
                            dataBuffer[key].TimeStamp = stamp;

                            string result = JsonConvert.SerializeObject(dataBuffer[key]);

                            //db.StringSet(key, result);
                            //DataValue dv = dataBuffer[key];
                            //dv.Value = value;
                            //dv.Updated = true;
                            //dv.TimeStamp = stamp;
                            //dataBuffer[key] = dv;
                        }

                        this.dataGridView.Rows[rowIndex].Cells[i + 1].Value = value;

                        //if (this.dataGridView.InvokeRequired)
                        //{
                        //    this.dataGridView.BeginInvoke(new MethodInvoker(() => {
                        //        this.dataGridView.Rows[rowIndex].Cells[i + 1].Value = value;
                        //    }));
                        //}
                        //else
                        //{
                        //    this.dataGridView.Rows[rowIndex].Cells[i + 1].Value = value;
                        //}

                    }
                }
                sb.Remove(sb.Length - 1, 1);
                //sb.Append("\r\n");
                times++;
                if (times == 30)
                {
                    times = 0;
                    AppendRecord(sb);
                }

            }
            else
            {
                log.Warn(Tag + "broken frame");
                this.errMsg = "broken frame";
                //message = this.deviceId + "strain broken frame: " + CVT.ByteToHexStr(by) + "\r\n";
            }
        }
        /// <summary>
        /// 写记录
        /// </summary>
        /// <param name="str"></param>
        private void AppendRecord(StringBuilder str)
        {
            //if (!Directory.Exists("ErrLog"))
            //{
            //    Directory.CreateDirectory("ErrLog");
            //}
            string currentDate = this.config.RemoteIpAddress + "_"+DateTime.Now.ToString("yyyy-MM-dd")+ ".txt";

            //string pathString = Path.Combine(@"D:\vibrate", currentDate);

            using (StreamWriter sw = new StreamWriter(currentDate, true))
            {
                sw.WriteLine(str);
                sw.Close();
            }
        }
    }
}