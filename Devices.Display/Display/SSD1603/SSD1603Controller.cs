using System;
using System.Collections.Generic;
using Windows.Devices.Gpio;
using Windows.Devices.I2c;
using Windows.Devices.Spi;
using Q.IoT.Devices.Core;
using System.Threading.Tasks;
using System.Diagnostics;

namespace Q.IoT.Devices.Display
{
    internal class TransmissionData
    {
        internal enum TransmissionDataTypes { Command, Data }
        internal TransmissionDataTypes Type { get; set; }
        internal byte[] Data { get; set; }
        internal TransmissionData(TransmissionDataTypes type)
        {
            Type = type;
            Data = new byte[0];
        }
    }
    #region class / strut
    public class SSD1603Controller
    {
        private I2cDevice _i2cDevic;
        private SpiDevice _spiDevice;
        private GpioPin _pinReset;
        private GpioPin _pinCmdData;

        //properties
        private BusTypes _busType;

        private Queue<TransmissionData> _dataQueue = new Queue<TransmissionData>();
        private TransmissionData _lastData = null;

        //constructor
        public SSD1603Controller(I2cDevice device, GpioPin pinReset = null)
        {
            _busType = BusTypes.I2C;
            _i2cDevic = device;
            _pinReset = pinReset;
            Empty();
            Debug.WriteLine(string.Format("SSD1603 controller on {0} created", _busType));
        }

        public SSD1603Controller(SpiDevice device, GpioPin pinCmdData, GpioPin pinReset = null)
        {
            _busType = BusTypes.SPI;
            _spiDevice = device;
            _pinCmdData = pinCmdData;
            _pinReset = pinReset;
            Empty();
            Debug.WriteLine(string.Format("SSD1603 controller on {0} created", _busType));
        }

        public async Task Reset()
        {
            if (_pinReset != null)
            {
                _pinReset.Write(GpioPinValue.Low);   //Put display into reset 
                await Task.Delay(1);                // Wait at least 3uS (We wait 1mS since that is the minimum delay we can specify for Task.Delay()
                _pinReset.Write(GpioPinValue.High);  //Bring display out of reset
            }
            await Task.Delay(100);                //Wait at least 100mS before sending commands
        }

        public bool Initialized
        {
            get
            {
                if (_busType == BusTypes.I2C && _i2cDevic == null)
                {
                    return false;
                }
                else if (_busType == BusTypes.SPI && (_spiDevice == null || _pinCmdData == null))
                {
                    return false;
                }

                return true;
            }
        }

        public void Empty()
        {
            _lastData = null;
            _dataQueue.Clear();
        }

        //
        private void Append(bool isCommand, params byte[] data)
        {
            //add I2C control flag when operation changed
            if (data == null)
            {
                return;
            }
            bool needI2CCtrlFlag = _busType == BusTypes.I2C;
            TransmissionData.TransmissionDataTypes currType = isCommand ? TransmissionData.TransmissionDataTypes.Command : TransmissionData.TransmissionDataTypes.Data;
            if (_lastData == null)
            {
                //first data
                _lastData = new TransmissionData(currType);

                needI2CCtrlFlag = needI2CCtrlFlag && true;
            }
            else if (_lastData.Type != currType)
            {
                //data type changed
                _dataQueue.Enqueue(_lastData);
                _lastData = new TransmissionData(currType);


                needI2CCtrlFlag = needI2CCtrlFlag && true;
            }
            else
            {
                //same type as previous
                needI2CCtrlFlag = false;
            }

            byte[] oriData = _lastData.Data;
            int oriSize = oriData.Length;
            int newSize = oriSize + data.Length + (needI2CCtrlFlag ? 1 : 0);

            //extend array
            Array.Resize(ref oriData, newSize);
            if (needI2CCtrlFlag)
            {
                //add I2C control flag
                oriData[oriSize++] = isCommand ? SSD1603.I2CTransmissionControlFlags.Command : SSD1603.I2CTransmissionControlFlags.Data;
            }

            //append data
            foreach (byte s in data)
            {
                oriData[oriSize++] = s;
            }

            _lastData.Data = oriData;
        }

        public bool Send()
        {
            if (!Initialized)
            {
                return false;
            }
            if (_lastData == null)
            {
                return false;
            }
            try
            {
                //queue the last data
                _dataQueue.Enqueue(_lastData);
                _lastData = null;

                foreach (TransmissionData td in _dataQueue)
                {
                    if (_busType == BusTypes.I2C)
                    {
                        _i2cDevic.Write(td.Data);
                    }
                    else if (_busType == BusTypes.SPI)
                    {
                        _pinCmdData.Write(td.Type == TransmissionData.TransmissionDataTypes.Command ? GpioPinValue.Low : GpioPinValue.High);
                        _spiDevice.Write(td.Data);
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine(string.Format("Failed to send data to {0} device", _busType), ex.Message);
                return false;
            }
            finally
            {
                Empty();
            }
        }

        #region extend for convenience
        public void AppendCommand(params byte[] cmds)
        {
            Append(true, cmds);
        }
        public void AppendData(params byte[] data)
        {
            Append(false, data);
        }

        public void SetCommand(params byte[] cmds)
        {
            Empty();
            AppendCommand(cmds);
        }
        public void SetData(params byte[] data)
        {
            Empty();
            AppendData(data);
        }
        public void SendCommand(params byte[] cmds)
        {
            SetCommand(cmds);
            Send();
        }
        public void SendData(params byte[] data)
        {
            SetData(data);
            Send();
        }
        #endregion
    }
    #endregion
}
