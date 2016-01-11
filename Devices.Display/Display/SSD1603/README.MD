##SSD1306 OLED Driver
- Works with both SPI and I2C interface.
- Configurable for different display
- Using Win2D for easily rendering

###Install
- from NuGet https://www.nuget.org/packages/Q.IoT.Devices.Display  
- OR download soucre build yourself

###Usage
```
//initialize either SpiDevice or I2cDevice
I2cDevice device = ....

//create display object
SSD1603 display = new SSD1603(Screen.OLED_128_32, device);

//draw something 
if (display.State == SSD1603.States.Ready) {
    //draw
    using (CanvasDrawingSession ds = _displaySpi.Render.CreateDrawingSession())
    {
        ds.Clear(SSD1603.BackgroundColor);
        ds.DrawText("Hello", 0, 0, SSD1603.ForeColor);
    }
    
    display.Display();
}
```

###Configuration
```
SPIDevice device = ....

//configure
SSD1603.SSD1603Configuration cfg_128_64 = SSD1603.CreateConfiguration(Screen.OLED_128_64);
cfg_128_64.CommonPinConfiguration = SSD1603.CommonPinConfigurationOptions.Alternative;
cfg_128_64.IsSegmentRemapped = true;
cfg_128_64.IsCommonScanDirectionRemapped = true;

SSD1603 display = new SSD1603(cfg_128_64, device, pinDataCmd, pinReset);
```

###Sample
https://github.com/q-life/Q.IoT/blob/master/Samples/StartupTask.cs

###References
https://github.com/adafruit/Adafruit_SSD1306
https://github.com/olikraus/u8glib
https://github.com/sely2k/WindowsIot-SPiDisplay-SSD1306
