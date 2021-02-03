using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Linq;
using System.Text;
using System.Threading;
using System.Net.Sockets;
using System.ComponentModel;
using System.Net;
using System.Windows.Forms;
using System.Collections.Concurrent;
using StackExchange.Redis;

namespace DataAcquisition
{
    //public class DataValue
    //{
    //    //public string Key;
    //    public string SensorId;
    //    public string TimeStamp;
    //    public string ValueType;
    //    public double Value;

    //    public DataValue(string id,string stamp,string type,double value)
    //    {
    //        this.SensorId = id;
    //        this.TimeStamp = stamp;
    //        this.ValueType = type;
    //        this.Value = value;
    //    }
    //}

    public class DataValue
    {
        public string SensorId { get; set; }
        public string TimeStamp { get; set; }
        public string ValueType { get; set; }
        public double Value { get; set; }
    }

    class UdpACT12x
    {
        private static readonly log4net.ILog log = log4net.LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);
        private string remoteIpAddress;
        private int remotePort;
        private int localPort;
        private string localIP;
        protected UdpClient udpServer;

        private BackgroundWorker backgroundWorker;
        protected ConnectionMultiplexer redis;
        private string errMsg;
        private bool isSuccess;

        public UdpACT12x(int localPort,string localIP,int remotePort,string remoteAddress, ConnectionMultiplexer redis)
        {
            this.remoteIpAddress = remoteAddress;
            this.localPort = localPort;
            this.localIP = localIP;
            this.remotePort = remotePort;
            backgroundWorker = new BackgroundWorker();
            //redis = ConnectionMultiplexer.Connect("localhost");

            //udpServer = new UdpClient(localPort);
            //udpServer.Client.ReceiveTimeout = 2000;
            //udpServer.Connect(remoteAddress, remotePort);

            backgroundWorker.WorkerReportsProgress = true;
            backgroundWorker.WorkerSupportsCancellation = true;
            backgroundWorker.DoWork += new System.ComponentModel.DoWorkEventHandler(this.backgroundWorker_DoWork);
            backgroundWorker.ProgressChanged += new System.ComponentModel.ProgressChangedEventHandler(this.backgroundWorker_ProgressChanged);
            backgroundWorker.RunWorkerCompleted += new System.ComponentModel.RunWorkerCompletedEventHandler(this.backgroundWorker_RunWorkerCompleted);
        }

        private void backgroundWorker_DoWork(object sender, DoWorkEventArgs e)
        {
            BackgroundWorker bgWorker = sender as BackgroundWorker;

            IPEndPoint remoteIpEndPoint = new IPEndPoint(IPAddress.Any, 0);

            while (true)
            {
                try
                {
                    // Blocks until a message returns on this socket from a remote host.
                    Byte[] receiveBytes = udpServer.Receive(ref remoteIpEndPoint);

                    if(receiveBytes.Length > 0)
                    {
                        this.ProcessData(receiveBytes, receiveBytes.Length);
                    }
   
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.ToString());
                    bgWorker.ReportProgress(0,ex.ToString());
                }

                if (bgWorker.CancellationPending == true)
                {
                    e.Cancel = true;
                    break;
                }
            }

            //udpServer.Close();
            //PostData();
        }

        private void backgroundWorker_ProgressChanged(object sender, ProgressChangedEventArgs e)
        {

        }

        private void backgroundWorker_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {

        }

        public virtual void ProcessData(byte[] buffer,int length)
        {
            
        }

        public virtual void Start()
        {
            try
            {
                //udpServer = new UdpClient(localPort);
                //udpServer.Connect(IPAddress.Parse(remoteIpAddress), remotePort);
                IPEndPoint iPEndPoint = new IPEndPoint(IPAddress.Parse(this.localIP), this.remotePort);
                udpServer = new UdpClient(iPEndPoint);
            }
            catch (Exception ex) { }

            if (!backgroundWorker.IsBusy)
            {
                backgroundWorker.RunWorkerAsync();
            }         
        }

        public virtual void Stop()
        {
            if (udpServer != null)
            {
                udpServer.Close();
            }
            if (backgroundWorker.IsBusy)
            {
                backgroundWorker.CancelAsync();
            }
            
        }

        //private bool FrameCheck(byte[] buffer, int length)
        //{
        //    if (length != 20)
        //    {
        //        return false;
        //    }
        //    if (buffer[0] == 192 && buffer[0] == 168 && buffer[0] == 0 && buffer[0] == 7)
        //    {
        //        return false;
        //    }
        //    else
        //    {
        //        return true;
        //    }
        //}

        public virtual string GetResultString()
        {
            return "";
        }

        public virtual string GetObjectType()
        {
            return "UdpACT12x";
        }

        
        /*
        private void SaveData(string id, string type, double value)
        {
            string stamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            Modle m = new Modle();
            m.SensorID = id;
            m.StampTime = stamp;
            m.VauleType = type;
            m.Values = value.ToString();
            this.dataList.Add(m);
            SaveToDatabase(id, type, value, stamp);
        }
        */
        private void SaveToDatabase(string sensorId, string type, double value, string stamp)
        {
            SQLiteConnection conn = null;
            SQLiteCommand cmd;

            string database = "Data Source = LongRui.db";

            try
            {
                conn = new SQLiteConnection(database);
                conn.Open();

                cmd = conn.CreateCommand();

                {
                    cmd.CommandText = "insert into data values('" + sensorId + "','" + stamp + "','" + type + "'," + value.ToString() + ")";
                }

                cmd.ExecuteNonQuery();

                conn.Close();
            }
            catch (Exception ex)
            {
                conn.Close();
                //this.Invoke((EventHandler)(
                //    delegate {
                //        textBoxLog.AppendText(ex.ToString() + "\r\n");
                //    }));
            }
        }
    }

    class UdpACT12xConfig
    {
        public string Database = "Data Source = BGK.db";
        public string RemoteIpAddress;
        public int RemotePort;
        public string LocalIP;
        public int LocalPort;
        public string DeviceId;

        public UdpACT12xConfig(string remoteIpAddress, int remotePort, int localPort,string localIP, string sensorId)
        {
            this.RemoteIpAddress = remoteIpAddress;
            this.RemotePort = remotePort;
            this.LocalIP = localIP;
            this.LocalPort = localPort;
            this.DeviceId = sensorId;
        }
    }

    class CurrentVoltageChannel
    {
        public string sensorId;
        public int channelNo;
        public double initValue;
        public string type;

        /// <summary>
        /// 
        /// </summary>
        private double outputRangeTop;
        private double outputRangeBottom;
        private double measureRangeTop;
        private double measureRangeBottom;

        private double currentValue;
        private bool isUpdated;

        /// <summary>
        /// output = a*x + b
        /// </summary>
        private double a;
        private double b;



        public CurrentVoltageChannel(string sensorId, int channelNo, double initValue, double outputRangeTop, double outputRangeBottom, double measureRangeTop, double measureRangeBottom, string type)
        {
            this.sensorId = sensorId;
            this.channelNo = channelNo;
            this.initValue = initValue;
            this.currentValue = 0;
            this.isUpdated = false;
            this.type = type;

            this.outputRangeTop = outputRangeTop;
            this.outputRangeBottom = outputRangeBottom;
            this.measureRangeTop = measureRangeTop;
            this.measureRangeBottom = measureRangeBottom;

            a = (measureRangeBottom - measureRangeTop) / (outputRangeBottom - outputRangeTop);
            b = (measureRangeBottom * outputRangeTop - outputRangeBottom * measureRangeTop) / (outputRangeTop - outputRangeBottom);

            a = Math.Round(a, 4);
            b = Math.Round(b, 4);
        }

        public double GetResult(byte[] channel)
        {
            int tmp = channel[0] * 256 + channel[1];

            double raw = tmp / 1000.0;

            raw = Math.Round(raw, 4);

            if (raw < outputRangeBottom)
            {
                raw = outputRangeBottom;
            }

            if (raw > outputRangeTop)
            {
                raw = outputRangeTop;
            }

            double result = Calculate(raw);

            return result;
        }

        public virtual double Calculate(double raw)
        {

            double result = a * raw + b - initValue;
            return Math.Round(result, 3);
        }

    }
}