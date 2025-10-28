using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO.Ports;
using System.Reflection.Metadata;
using System.Text.RegularExpressions;

namespace PlotterNew.Services
{
    internal class ArduinoData
    {
        private SerialPort _port;
        private string _name;
        private int _boundRate;
        public ArduinoData(string name, int boundRate)
        {
            _name = name;
            _boundRate = boundRate;
            _port = new SerialPort(_name, _boundRate);
            _port.Open();
            System.Diagnostics.Debug.WriteLine("ArduinoData(): com port is opened");
        }

        private string ReadData()
        {
            return _port.ReadExisting();
        }

        static private List<int> ParseData(string data)
        {
            List<int> parsedData = new List<int> { };

            int firstSemicolonIndex = 0;
            int secondSemicolonIndex = 0;
            int dataSize = data.Length;
            int dataUnit;
            string strDataUnit = "null";

            bool isSuccessConverting = false;

            while (firstSemicolonIndex < dataSize)
            {
                firstSemicolonIndex = data.IndexOf(';', secondSemicolonIndex);
                if (firstSemicolonIndex < 0)
                {
                    return parsedData;
                }

                secondSemicolonIndex = data.IndexOf(";", firstSemicolonIndex + 1);
                if (secondSemicolonIndex < 0)
                {
                    return parsedData;
                }


                try
                {
                    strDataUnit = data.Substring(firstSemicolonIndex + 1, secondSemicolonIndex - 1);
                }
                catch (Exception e)
                {
                    System.Diagnostics.Debug.WriteLine("ParseData()::Substring()::index out of baunds");
                }

                isSuccessConverting = int.TryParse(strDataUnit, out dataUnit);
                if (!isSuccessConverting)
                {
                    System.Diagnostics.Debug.WriteLine("ParseData()::can't parse such data");
                    break;
                }

                firstSemicolonIndex = secondSemicolonIndex;
                parsedData.Add(dataUnit);
            }
            return parsedData;
        }

        private string ParseArduinoPlotDataLine(string serialData)
        {
            int start = serialData.IndexOf('\n');

            while (start < 0)
            {
                serialData += ReadData();

                start = serialData.IndexOf('\n', start + 1);
            }

            int end = serialData.IndexOf('\n', start + 1);

            while (end < 0)
            {
                serialData += ReadData();

                end = serialData.IndexOf('\n', start + 1);
            }

            return serialData.Substring(start + 1, end - start - 1) + "\n";
        }

        public static List<double> ParseSignals(string line, int valuesAmount)
        {
            // Result pre-filled with zeros
            var result = new List<double>(capacity: valuesAmount);
            for (int i = 0; i < valuesAmount; i++)
                result.Add(0.0);

            if (string.IsNullOrWhiteSpace(line))
                return result;

            // Remove trailing newline(s) and surrounding whitespace
            line = line.Trim();

            // Split by commas into tokens like "s3:-0.1875"
            var tokens = line.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            foreach (var token in tokens)
            {
                // Expect "name:value"
                var parts = token.Split(':', 2, StringSplitOptions.TrimEntries);
                if (parts.Length != 2) continue;

                var name = parts[0];   // e.g., "s3"
                var valStr = parts[1]; // e.g., "-0.1875"

                if (name.Length >= 2 && (name[0] == 's' || name[0] == 'S'))
                {
                    // Try parse index after 's'
                    if (int.TryParse(name.AsSpan(1), out int idx) && idx >= 0 && idx < valuesAmount)
                    {
                        if (double.TryParse(valStr, NumberStyles.Float | NumberStyles.AllowThousands,
                                            CultureInfo.InvariantCulture, out double value))
                        {
                            result[idx] = value;
                        }
                        else
                        {
                            // If value malformed, leave default 0.0
                        }
                    }
                }
            }

            return result;
        }

        public List<double> GetArduinoPlotData(int valuesAmount)
        {
            string arduinoPlotDataLine = GetLineData();
            return ParseSignals(arduinoPlotDataLine, valuesAmount);
        }
        public string GetLineData()
        {
            return ParseArduinoPlotDataLine(ReadData());
        }

        public List<int> GetData()
        {
            return ParseData(ReadData());
        }

        public void sendData(String data)
        {
            _port.Write(data);
        }
    }
}
