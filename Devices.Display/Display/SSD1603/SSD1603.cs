using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Windows.Devices.Enumeration;
using Windows.Devices.I2c;
using Windows.Devices.Spi;
using Windows.Devices.Gpio;
using Microsoft.Graphics.Canvas;
using Windows.UI;
using Q.IoT.Devices.Core;
namespace Q.IoT.Devices.Display
{
    public partial class SSD1603
    {
        //configuration
        public SSD1603Configuration Configuration { get; private set; }

        //display part
        public Screen Screen { get { return Configuration.Screen; } }
        private byte[] _buffer;

        //I/O
        public BusTypes BusType { get; private set; }
        private SSD1603Controller _controller;

        //draw
        private CanvasDevice _canvasDevice = new CanvasDevice();
        public static readonly Color ForeColor = Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF);
        public static readonly Color BackgroundColor = Color.FromArgb(0x00, 0x00, 0x00, 0x00);

        //public properties
        public CanvasRenderTarget Render { get; private set; }
        public States State { get; private set; } = States.Unknown;

        /// <summary>
        /// constructor
        /// </summary>
        /// <param name="config"></param>
        private SSD1603(SSD1603Configuration config, BusTypes bus)
        {
            Configuration = config;
            BusType = bus;

            //for drawing
            _canvasDevice = CanvasDevice.GetSharedDevice();
            Render = new CanvasRenderTarget(_canvasDevice,  Screen.WidthInDIP, Screen.HeightInDIP, Screen.DPI,
                            Windows.Graphics.DirectX.DirectXPixelFormat.A8UIntNormalized, CanvasAlphaMode.Straight);
        }
        
        //I2c constructor
        public SSD1603(SSD1603Configuration config, I2cDevice device, GpioPin pinReset = null) : this(config, BusTypes.I2C)
        {
            _controller = new SSD1603Controller(device, pinReset);
            Init().Wait();
        }
        public SSD1603(Screen screen, I2cDevice device, GpioPin pinReset = null) : this(new SSD1603Configuration(screen), device, pinReset)
        {
        }

        //SPI constructor
        public SSD1603(SSD1603Configuration config, SpiDevice device, GpioPin pinCmdData, GpioPin pinReset = null) : this(config, BusTypes.SPI)
        {
            _controller = new SSD1603Controller(device, pinCmdData, pinReset);
            Init().Wait();
        }
        public SSD1603(Screen screen, SpiDevice device, GpioPin pinCmdData, GpioPin pinReset = null) : this(new SSD1603Configuration(screen), device, pinCmdData, pinReset)
        {
        }

        public static SSD1603Configuration CreateConfiguration(Screen screen)
        {
            return new SSD1603Configuration(screen);
        }

        #region initialize
        /// <summary>
        /// initialize
        /// </summary>
        /// <returns></returns>
        public async Task Init()
        {
            State = States.Initializing;

            if (!_controller.Initialized)
            {
                State = States.Abroted;
                Debug.WriteLine(string.Format("failed to initialize SSD1603 display on {0}", BusType));
                return;
            }
            if (!await InitDisplay())
            {
                State = States.Abroted;
                Debug.WriteLine(string.Format("failed to initialize SSD1603 display on {0}", BusType));
                return;
            }
            State = States.Ready;
            Debug.WriteLine(string.Format("SSD1603 display on {0} initialized", BusType));
            return;
        }
  
        /// <summary>
        /// initialize display
        /// </summary>
        /// <returns></returns>
        private async Task<bool> InitDisplay()
        {
            try
            {
                //See the datasheet for more details on these commands: http://www.adafruit.com/datasheets/SSD1306.pdf 
                if (!_controller.Initialized)
                {
                    return false;
                }

                await _controller.Reset();

                //initialize command / configuration
                _controller.SetCommand(FundamentalCommands.DisplayOff);
                _controller.AppendCommand(TimingAndDrivingSchemeCommands.SetDivideRatioAndOscillatorFrequency, Configuration.DivdeRatioAndOscillatorFreuency);
                _controller.AppendCommand(HardwareConfigurationCommands.SetMultiplexRatio, Configuration.MultiplexRatio);    //MUX(duty) = common(height of screen)
                _controller.AppendCommand(HardwareConfigurationCommands.SetDisplayOffset, Configuration.DisplayOffset);
                _controller.AppendCommand(Configuration.DisplayStartLine);
                _controller.AppendCommand(Configuration.IsSegmentRemapped? 
                                            HardwareConfigurationCommands.SetSegmentRemapReverse:
                                            HardwareConfigurationCommands.SetSegmentRemap); //left-right
                _controller.AppendCommand(Configuration.IsCommonScanDirectionRemapped? 
                                            HardwareConfigurationCommands.SetComOutputScanDirectionReverse: 
                                            HardwareConfigurationCommands.SetComOutputScanDirection); //up - down
                _controller.AppendCommand(HardwareConfigurationCommands.SetComPinsHardwareConfiguration, (byte)Configuration.CommonPinConfiguration); //need configure
                _controller.AppendCommand(FundamentalCommands.SetContrast, Configuration.Contrast);
                _controller.AppendCommand(AddressingCommands.SetMemoryAddressingMode, Configuration.MemoryAddressingMode);
                _controller.AppendCommand(AddressingCommands.SetPageAddress, Configuration.StartPageAddress, Configuration.EndPageAddress);       //set page address
                _controller.AppendCommand(AddressingCommands.SetColumnAddress, Configuration.StartColumnAddress, Configuration.EndColumnAddress);     //set column address
                _controller.AppendCommand(TimingAndDrivingSchemeCommands.SetPreChargePreiod, Configuration.PreChargePreiod);
                _controller.AppendCommand(TimingAndDrivingSchemeCommands.SetComDeselectVoltageLevel, Configuration.ComDeselectVoltageLevel);
                _controller.AppendCommand(ScrollingCommand.DeactivateScroll);
                _controller.AppendCommand(FundamentalCommands.NormalDisplay);
                _controller.AppendCommand(FundamentalCommands.SetChargePump, Configuration.ChargePumpSetting);
                _controller.AppendCommand(FundamentalCommands.DisplayOn);
                _controller.Send();

                //clear
                InitBuffer();
                DisplayBuffer();
                await Task.Delay(1000);
                return true;
            }
            catch
            {
                return false;
            }
        }
        #endregion

        #region drawing functions
        /// <summary>
        /// generate logo for initialize buffer
        /// </summary>
        /// <param name="resolution"></param>
        /// <returns></returns>
        private void InitBuffer()
        {
            if (Screen == Screen.OLED_128_32)
            {
                _buffer = Logos.LOGO_128_32;
            }
            else if (Screen == Screen.OLED_128_64)
            {
                _buffer = Logos.LOGO_128_64;
            }
            else if (Screen == Screen.OLED_64_32)
            {
                _buffer = Logos.LOGO_64_32;
            }
            else
            {
                _buffer = new byte[] { };
            }
        }

        private void MapCanvasToBuffer()
        {
            byte[] rawData = Render.GetPixelBytes();
            int page = 0;
            int col = 0;
            int pixelIdx = 0;
            for (int bufferIdx = 0; bufferIdx < _buffer.Length; bufferIdx++)
            {
                byte value = 0x00;
                for (byte rowInPage = 0; rowInPage < NumberOfCommonsPerPage; rowInPage++)
                {
                    if (IsPixelOn(rawData[pixelIdx + Screen.WidthInPixel * rowInPage]))
                    { 
                        //ON
                        value = (byte)(value | (1 << rowInPage));
                    }
                    else
                    {
                        //OFF
                        value = (byte)(value & ~(1 << rowInPage));
                    }
                }


                _buffer[bufferIdx] = value;

                pixelIdx++;
                if (++col == Screen.WidthInPixel)
                {
                    col = 0;
                    page++;
                    pixelIdx = 0;
                    pixelIdx = Screen.WidthInPixel * NumberOfCommonsPerPage * page;
                }
            }
        }

        private bool IsPixelOn(byte value)
        {
            int diffBg = value - BackgroundColor.A;
            int diffFore = ForeColor.A - value;
            return diffFore < diffBg;
        }
        private void DisplayBuffer()
        {
            _controller.SendData(_buffer);
        }
        public void Clear()
        {
            using (CanvasDrawingSession ds = Render.CreateDrawingSession())
            {
                ds.Clear(Color.FromArgb(0xFF, 0, 0, 0));
            }
            Array.Clear(_buffer, 0, _buffer.Length);
            DisplayBuffer();
        }

        public void Display()
        {
            MapCanvasToBuffer();
            DisplayBuffer();
        }
        #endregion
    }
}
