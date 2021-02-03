using DataAcquisition;
using StackExchange.Redis;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Data.SQLite;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace ACT12xCurrent
{
    public partial class Form1 : Form
    {
        private static readonly log4net.ILog log = log4net.LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);
        private Dictionary<string, UdpACT12x> deviceList;
        /// <summary>
        /// 缓存采集到的数据，用于计算基于压力的挠度和数据保存
        /// 
        /// </summary>
        private Dictionary<string, DataValue> DataBuffer;

        private List<string> sensorIds;
        private string database = "Data Source = BGK.db";

        private BackgroundWorker calculateDeflectionWorker;
        private BackgroundWorker saveDataWorker;
        private ConnectionMultiplexer redis;
        private System.Timers.Timer timer;
        private bool serviceIsStop;

        public Form1()
        {
            //008
            InitializeComponent();
            serviceIsStop = true;
            DataBuffer = new Dictionary<string, DataValue>();
            deviceList = new Dictionary<string, UdpACT12x>();
            sensorIds = new List<string>();
            redis = ConnectionMultiplexer.Connect("localhost,abortConnect=false");
            LoadChannels();
            LoadDevices();

            ToolStripMenuItemStart.Enabled = true;
            ToolStripMenuItemStop.Enabled = false;

            timer = new System.Timers.Timer();
            timer.Elapsed += Timer_Elapsed;

            calculateDeflectionWorker = new BackgroundWorker();

            calculateDeflectionWorker.WorkerSupportsCancellation = true;
            calculateDeflectionWorker.DoWork += new System.ComponentModel.DoWorkEventHandler(calculateDeflectionWorker_DoWork);

            saveDataWorker = new BackgroundWorker();
            saveDataWorker.WorkerSupportsCancellation = true;
            saveDataWorker.DoWork += new System.ComponentModel.DoWorkEventHandler(saveDataWorker_DoWork);
        }

        private void Timer_Elapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            if (saveDataWorker.IsBusy)
            {
                this.Invoke((EventHandler)(
                delegate
                {
                    textBoxLog.AppendText("正在采集数据，无法开始新的采集周期" + "\r\n");
                }));
            }
            else
            {
                saveDataWorker.RunWorkerAsync();
            }
        }

        /// <summary>
        /// 返回Pi相对于参考点P2的位移
        /// </summary>
        /// <param name="p1">参考点P1</param>
        /// <param name="p2">参考点P2</param>
        /// <param name="pi">测点Pi</param>
        /// <returns></returns>
        private double CalculateDeflection(double p1,double p2,double pi)
        {
            //unit mm
            double L = 1393.308;

            double deflection = L * (pi - p2) / (p2 - p1);

            return deflection;
        }

        private void saveDataWorker_DoWork(object sender, DoWorkEventArgs e)
        {
            BackgroundWorker bgWorker = sender as BackgroundWorker;
            string datasource = "Data Source = BGK.db";

            string message = "";

            string stamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

            //while (true)
            //{

            using (SQLiteConnection con = new SQLiteConnection(database))
            {
                con.Open();
                using (SQLiteTransaction tran = con.BeginTransaction())
                {
                    using (SQLiteCommand cmd = new SQLiteCommand(con))
                    {
                        cmd.Transaction = tran;
                        try
                        {
                            int count = DataBuffer.Values.Count;
                            DataValue[] array = new DataValue[count];
                            DataBuffer.Values.CopyTo(array, 0);
                            foreach (DataValue item in array)
                            {
                                //if (item.Updated)
                                {
                                    cmd.CommandText = "insert into data values('" + item.SensorId + "','" + stamp + "','" + item.ValueType + "'," + item.Value.ToString() + ")";
                                    cmd.ExecuteNonQuery();
                                }
                            }
                            tran.Commit();
                        }
                        catch (Exception ex)
                        {
                            //MessageBox.Show(ex.Message);

                            this.Invoke((EventHandler)(
                                delegate
                                {
                                    textBoxLog.AppendText(ex.Message + "\r\n");
                                }));

                            tran.Rollback();
                        }
                    }
                }
            }

            //foreach (DataValue item in DataBuffer.Values)
            //    {
            //        if (item.Updated)
            //        {
            //            message += item.SensorId + "," + stamp + "'" + item.ValueType + "," + item.Value.ToString() + "\r\n";
            //            //cmd.ExecuteNonQuery();
            //        }
            //    }
            this.Invoke((EventHandler)(
                     delegate
                     {
                         textBoxLog.AppendText("result: " + message + "\r\n--------------------------------------------\r\n");
                     }));

                //if (bgWorker.CancellationPending == true)
                //{
                //    e.Cancel = true;
                //    //break;
                //}

                //Thread.Sleep(5000);
            //}
        }

        private void calculateDeflectionWorker_DoWork(object sender, DoWorkEventArgs e)
        {
            BackgroundWorker bgWorker = sender as BackgroundWorker;
            string datasource = "Data Source = BGK.db";

            string message = "";

            while (true)
            {
                string stamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                string refPoint1 = "5600001710001-008";
                string refPoint2 = "5600001710008-008";
                double pressurePoint1 = 0;
                double pressurePoint2 = 0;

                //计算挠度
                if (DataBuffer.ContainsKey(refPoint1))
                {
                    DataValue point1 = DataBuffer[refPoint1];
                    //message += point1.SensorId + " " + point1.ValueType + " " + point1.Updated + "\r\n";
                    //if (point1.Updated)
                    {
                        pressurePoint1 = point1.Value;
                        if (DataBuffer.ContainsKey(refPoint2))
                        {
                            DataValue point2 = DataBuffer[refPoint2];
                            //message += point2.SensorId + " " + point2.ValueType + " " + point2.Updated + "\r\n";
                            //if (point2.Updated)
                            {
                                pressurePoint2 = point2.Value;
                                foreach (string item in sensorIds)
                                {
                                    if (item == refPoint1 || item == refPoint2)
                                    {
                                        continue;
                                    }

                                    if (DataBuffer.ContainsKey(item))
                                    {
                                        DataValue dv = DataBuffer[item];
                                        
                                        //message += dv.SensorId + " " + dv.ValueType + " " + dv.Updated + "\r\n";
                                        //if (dv.Updated)
                                        {
                                            double pressurePointI = dv.Value;

                                            double deflection = CalculateDeflection(pressurePoint1, pressurePoint2, pressurePointI);

                                            //message += dv.SensorId + " defelection : " + deflection.ToString() + "\r\n";

                                            string key = dv.SensorId + "-" + "010";
                                            if (DataBuffer.ContainsKey(key))
                                            {
                                                DataValue dataValue = DataBuffer[key];
                                                dataValue.Value = deflection;
                                                dataValue.TimeStamp = stamp;
                                                //dataValue.Updated = true;
                                                //DataBuffer[key] = dataValue;
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }

                //if (DataBuffer.ContainsKey("5600001701001-004"))
                //{
                //    message += "key 5600001701001-004 really exist\r\n";
                //}
                //else
                //{
                //    message += "key 5600001701001-004 not exist\r\n";
                //}

                //if (DataBuffer.ContainsKey("5600001701001-011"))
                //{
                //    message += "key 5600001701001-011 really exist\r\n";
                //}
                //else
                //{
                //    message += "key 5600001701001-011 not exist\r\n";
                //}

                //计算风向
                //"014" ---水平风向   "015" ---垂直风向
                if (DataBuffer.ContainsKey("5600001701003-004") && DataBuffer.ContainsKey("5600001701003-011"))
                {
                    DataValue speed3D = DataBuffer["5600001701003-004"];
                    DataValue angle = DataBuffer["5600001701003-011"];

                    //message += "key 5600001701001-004 exist";

                    //if (speed3D.Updated && angle.Updated)
                    {
                        double speedHorizontal = speed3D.Value * Math.Cos(angle.Value * Math.PI / 180);
                        double speedVertical = speed3D.Value * Math.Sin(angle.Value * Math.PI / 180);
                        
                        message += "Angle: " + angle.Value + "\r\n";

                        if (DataBuffer.ContainsKey("5600001701003-014")){
                            DataValue dv1 = DataBuffer["5600001701003-014"]; 
                            dv1.TimeStamp = stamp;
                            dv1.Value = speedHorizontal;
                            //dv1.Updated = true;
                            message += dv1.SensorId + " " + dv1.ValueType + " " + dv1.Value + "\r\n";
                        }
                        else
                        {
                            DataValue speed1 = new DataValue();
                            speed1.SensorId = "5600001701003";
                            speed1.Value = speedHorizontal;
                            speed1.TimeStamp = stamp;
                            speed1.ValueType = "014";
                            //speed1.Updated = true;
                            DataBuffer.Add("5600001701003-014", speed1);

                            message += speed1.SensorId + " " + speed1.ValueType + " " + speed1.TimeStamp + "\r\n";
                        }

                        if (DataBuffer.ContainsKey("5600001701003-015"))
                        {
                            DataValue dv2 = DataBuffer["5600001701003-015"];
                            dv2.TimeStamp = stamp;
                            dv2.Value = speedVertical;
                            //dv2.Updated = true;
                            message += dv2.SensorId + " " + dv2.ValueType + " " + dv2.Value + "\r\n";
                        }
                        else
                        {
                            DataValue speed = new DataValue();
                            speed.SensorId = "5600001701003";
                            speed.Value = speedVertical;
                            speed.TimeStamp = stamp;
                            speed.ValueType = "015";
                            //speed.Updated = true;
                            DataBuffer.Add("5600001701003-015", speed);
                            message += speed.SensorId + " " + speed.ValueType + " " + speed.TimeStamp + "\r\n";
                        }
                    }
                }
                else
                {
                    //message += "key 5600001701001-004 not exist";
                }

                this.Invoke((EventHandler)(
                     delegate
                     {
                         textBoxLog.AppendText("result: " + message + "\r\n--------------------------------------------\r\n");
                         message = "";
                     }));

                if (bgWorker.CancellationPending == true)
                {
                    e.Cancel = true;
                    break;
                }

                Thread.Sleep(1000);
            }
        }

        private void LoadChannels()
        {
            using (SQLiteConnection connection = new SQLiteConnection(database))
            {
                connection.Open();
                //string idHead = config.SensorId.Substring(0, config.SensorId.Length - 3);
                string stamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                string strainStatement = "select SensorId,Type from CVChannels";
                SQLiteCommand command = new SQLiteCommand(strainStatement, connection);
                using (SQLiteDataReader reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        string sensorId = reader.GetString(0);
                        string type = reader.GetString(1);

                        DataValue dv= new DataValue();
                        string key = sensorId + "-" + type;

                        dv.SensorId = sensorId;
                        dv.TimeStamp = stamp;
                        dv.ValueType = type;
                        dv.Value = 0;
                        //dv.Updated = false;

                        DataBuffer.Add(key, dv);

                        //update sensorIds
                        if(type == "008")
                        {
                            sensorIds.Add(key);

                            DataValue dv_deflection = new DataValue();
                            string key_deflection = sensorId + "-" + "010";

                            dv_deflection.SensorId = sensorId;
                            dv_deflection.TimeStamp = stamp;
                            dv_deflection.ValueType = "010";
                            dv_deflection.Value = 0;
                            //dv_deflection.Updated = false;

                            DataBuffer.Add(key_deflection, dv_deflection);
                        }
                    }
                }
                connection.Close();
            }
        }

        private void LoadDevices()
        {
            this.deviceList.Clear();
            using (SQLiteConnection connection = new SQLiteConnection(database))
            {
                connection.Open();
                //string deviceType = "ACT12816";
                string strainStatement = "select DeviceId,RemoteIP,RemotePort,LocalPort,Type,Desc,LocalIP from SensorInfo";
                SQLiteCommand command2 = new SQLiteCommand(strainStatement, connection);
                using (SQLiteDataReader reader = command2.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        string deviceId = reader.GetString(0);
                        string remoteIP = reader.GetString(1);
                        int RemotePort = reader.GetInt32(2);
                        int LocalPort = reader.GetInt32(3);
                        string deviceType = reader.GetString(4);
                        string desc = reader.GetString(5);
                        string localIP = reader.GetString(6);
                        int index = this.dataGridView1.Rows.Add();

                        string[] itemString = { desc, deviceType, deviceId, remoteIP, RemotePort.ToString(), LocalPort.ToString(),localIP };
                        ListViewItem item = new ListViewItem(itemString);

                        listView1.Items.Add(item);

                        UdpACT12xConfig config = new UdpACT12xConfig(remoteIP, RemotePort, LocalPort, localIP, deviceId);

                        UdpACT12x device = null;

                        this.dataGridView1.Rows[index].Cells[0].Value = desc;

                        if (deviceType == "ACT1218")
                        {
                            device = new UdpACT1218(config, this.dataGridView1, index,this.DataBuffer,redis);
                        }
                        else if (deviceType == "ACT12816")
                        {
                            device = new UdpACT12816(config, this.dataGridView1, index,this.DataBuffer,redis);
                        }
                        else { }

                        //device.Start();

                        if (device != null)
                        {
                            this.deviceList.Add(deviceId, device);
                        }
                    }
                }
            }
        }

        //private void StartAcquisit()
        //{
        //    serviceIsStop = false;
        //    buttonStart.Enabled = false;
        //    buttonStop.Enabled = true;
        //}

        //private void StopAcquisit()
        //{
        //    serviceIsStop = true;
        //    buttonStart.Enabled = true;
        //    buttonStop.Enabled = false;
        //}

        //private void buttonStart_Click(object sender, EventArgs e)
        //{
        //    buttonStart.Enabled = false;
        //    buttonStop.Enabled = true;
        //    foreach (var item in this.deviceList)
        //    {
        //        item.Value.Start();
        //    }
        //}

        //private void buttonStop_Click(object sender, EventArgs e)
        //{
        //    buttonStart.Enabled = true;
        //    buttonStop.Enabled = false;
        //    foreach (var item in this.deviceList)
        //    {
        //        item.Value.Stop();
        //    }
        //}

        private void buttonStopTest_Click(object sender, EventArgs e)
        {
            //this.buttonStart.Enabled = true;
            //udpACT12816.Stop();
        }

        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            // 注意判断关闭事件reason来源于窗体按钮，否则用菜单退出时无法退出!
            if (e.CloseReason == CloseReason.UserClosing)
            {
                //取消"关闭窗口"事件
                e.Cancel = true; // 取消关闭窗体 

                //使关闭时窗口向右下角缩小的效果
                this.WindowState = FormWindowState.Minimized;
                this.notifyIcon1.Visible = true;
                this.Hide();
                return;
            }
        }

        private void RestoreToolStripMenuItem_Click(object sender, EventArgs e)
        {
            this.Visible = true;
            this.WindowState = FormWindowState.Normal;
            this.notifyIcon1.Visible = true;
            this.Show();
        }

        private void ExitToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (MessageBox.Show("确定要退出？", "系统提示", MessageBoxButtons.YesNo, MessageBoxIcon.Information, MessageBoxDefaultButton.Button1) == DialogResult.Yes)
            {
                if (ToolStripMenuItemStop.Enabled)
                {
                    //buttonStop_Click(null, null);
                    foreach (var item in this.deviceList)
                    {
                        item.Value.Stop();
                    }
                }
                
                this.notifyIcon1.Visible = false;
                this.Close();
                this.Dispose();
            }
        }

        private void notifyIcon1_MouseDoubleClick(object sender, MouseEventArgs e)
        {
            if (this.Visible)
            {
                this.WindowState = FormWindowState.Minimized;
                this.notifyIcon1.Visible = true;
                this.Hide();
            }
            else
            {
                this.Visible = true;
                this.WindowState = FormWindowState.Normal;
                this.Activate();
            }
        }

        private void ToolStripMenuItemStart_Click(object sender, EventArgs e)
        {
            ToolStripMenuItemStart.Enabled = false;
            ToolStripMenuItemStop.Enabled = true;
            foreach (var item in this.deviceList)
            {
                item.Value.Start();
            }
        }

        private void ToolStripMenuItemStop_Click(object sender, EventArgs e)
        {
            ToolStripMenuItemStart.Enabled = true;
            ToolStripMenuItemStop.Enabled = false;
            foreach (var item in this.deviceList)
            {
                item.Value.Stop();
            }
        }

        private void dataGridView1_CellContentClick(object sender, DataGridViewCellEventArgs e)
        {

        }
    }
}
