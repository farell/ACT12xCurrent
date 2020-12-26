using Newtonsoft.Json;
using StackExchange.Redis;
using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Windows.Forms;

namespace DataAcquisition
{

    class UdpACT12816 : UdpACT12x
    {
        private static readonly log4net.ILog log = log4net.LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);
        private DataGridView dataGridView;
        private Dictionary<int, CurrentVoltageChannel> channels;
        private UdpACT12xConfig config;
        private int rowIndex;
        private string errMsg;
        private const int NumberOfChannels = 16;
        private Dictionary<string, DataValue> dataBuffer; 
        private int count;
        //
        private IDatabase db;
        private string Tag;

        public UdpACT12816(UdpACT12xConfig config, DataGridView dataGridView,int rowIndex, Dictionary<string, DataValue> valueMap, ConnectionMultiplexer redis) : base(config.LocalPort, config.RemotePort, config.RemoteIpAddress,redis)
        {
            this.Tag = config.RemoteIpAddress + " : ";
            this.dataGridView = dataGridView;
            this.dataBuffer = valueMap;
            this.config = config;
            this.rowIndex = rowIndex;
            this.count = 0;
            channels = new Dictionary<int, CurrentVoltageChannel>();
            //
            
            LoadChannels();
            db = redis.GetDatabase();
        }

        private void LoadChannels()
        {
            using (SQLiteConnection connection = new SQLiteConnection(this.config.Database))
            {
                connection.Open();
                string strainStatement = "select SensorId,ChannelNo,InitValue,OutputRangeTop,OutputRangeBottom,MeasureRangeTop,MeasureRangeBottom,Type from CVChannels where GroupNo ='" + config.DeviceId + "'";
                SQLiteCommand command = new SQLiteCommand(strainStatement, connection);
                using (SQLiteDataReader reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
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
            if (length != 35)
            {
                return false;
            }
            if (buffer[0] == 0x00 && buffer[1] == 0x03 && buffer[2] == 0x20)
            {
                return true;
            }
            else
            {
                return false;
            }
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
            this.udpServer.Send(start,start.Length);
        }

        public override void Stop()
        {
            byte[] stop = { 0x00, 0x10, 0x00, 0xc8, 0x00, 0x01, 0x01, 0x00 };
            this.udpServer.Send(stop, stop.Length);
            base.Stop();
            //redis.Close();
        }

        public override void ProcessData(byte[] buffer, int length)
        {

            bool checkPassed = this.FrameCheck(buffer, length);
            if (checkPassed == true)
            {
                int startIndex = 3;

                string message = "";

                string stamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

                for (int i = 0; i < NumberOfChannels; i++)
                {

                    byte[] bytes = new byte[2];
                    bytes[0] = buffer[startIndex + i * 2];
                    bytes[1] = buffer[startIndex + i * 2 + 1];

                    if (channels.ContainsKey(i+1))
                    {
                        CurrentVoltageChannel cvc = channels[i + 1];

                        if (cvc != null)
                        {
                            double value = cvc.GetResult(bytes);

                            string key = cvc.sensorId + "-" + cvc.type;

                            //DataValue dv = new DataValue();

                            if (dataBuffer.ContainsKey(key))
                            {
                                dataBuffer[key].Value = Math.Round(value,3);
                                //dataBuffer[key].Updated = true;
                                dataBuffer[key].TimeStamp = stamp;

                                string result = JsonConvert.SerializeObject(dataBuffer[key]);

                                db.StringSet(key,result);
                                //dataBuffer[key].Value = value;
                                //DataValue dv = dataBuffer[key];
                                //dv.Value = value;
                                //dv.Updated = true;
                                //dv.TimeStamp = stamp;
                                //dataBuffer[key] = dv;
                                //this.dataGridView.Rows[rowIndex].Cells[i + 1].Value = key;
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
                    message += "通道" + (i + 1) + " Value: " + "\r\n";
                }
            }
            else
            {
                log.Warn(Tag+ "broken frame");
                this.errMsg = "broken frame";
                //message = this.deviceId + "strain broken frame: " + CVT.ByteToHexStr(by) + "\r\n";
            }
        }
    }
}