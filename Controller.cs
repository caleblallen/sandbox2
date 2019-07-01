﻿using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Net;
using SnmpSharpNet;

namespace AtwaterMonitor
{
    public class ImproperOidsException : Exception
    {
        public ImproperOidsException()
        {
        }
        public ImproperOidsException(string message) : base(message)
        {
        }

        public ImproperOidsException(string message, Exception inner) : base(message, inner)
        {
        }
    }
    class Controller
    { //SnmpSharpNet.SnmpException
        private string[] UpsIpAddresses;

        public bool init()
        {
            //Can't forget about our unit tests
            TestDriver();

            //Create the master Model
            Model AtwaterMonitorModel = new Model();

            //Initialize our UPS IPAddresses
            UpsIpAddresses = new string[] {
                "10.10.200.110",
                "10.10.200.111",
                "10.10.200.112",
                "10.10.200.113",
                "10.10.200.114",
                "10.10.200.115",
                "10.10.180.100",
                "10.10.180.101",
                "10.10.180.102",
                "10.10.180.103",
                "10.10.180.104",
                "10.10.180.105",
                "10.10.180.106",
                "10.10.180.107",
                "10.10.180.108",
                "10.10.170.100",
                "10.10.170.101",
                "10.10.170.102",
                "10.10.170.103",
                "10.10.170.104",
                "10.10.190.100",
                "10.10.190.101",
                "10.10.190.102",
                "10.10.190.103",
                "10.10.190.104",
                "10.10.190.144",
                "10.10.190.147",
                "10.10.210.100",
                "10.10.210.101",
                "10.10.210.102",
                "10.10.210.103",
                "10.10.210.104"};

            foreach (string ip in UpsIpAddresses)
            {
                Console.WriteLine(CreateApcUpsAtIp(ip));
                //SnmpTest(ip);
            }

            //TODO Fix this.
            return true;

        }

        private UPS CreateApcUpsAtIp(string ipAddress)
        {
            Dictionary<string, string> snmpResults;

            KeyValuePair<string,string> temperatureResult = new KeyValuePair<string, string>("","");

            int tProbeOidIndex = 0;

            try
            {
                //First we try to get our intial device info.
                snmpResults = GetSnmpInfo(ipAddress, UPS.Oids);

                //Next we try to find a temperature probe in one of 4 locations.
                do
                {
                    try
                    {
                        temperatureResult = GetSnmpResultForOid(ipAddress, UPS.TemperatureProbeOids[tProbeOidIndex]);
                    }
                    catch (ImproperOidsException e)
                    {
                        //We anticipate some SNMP calls to fail because of an incorrect Oids.
                        tProbeOidIndex++;
                    }
                } while (tProbeOidIndex < UPS.TemperatureProbeOids.Count() && temperatureResult.Equals(new KeyValuePair<string,string>("","")));
            }
            //An SnmpException means we either have no device to reach, or we're hitting a device without SNMP v1 running and responding.
            catch (SnmpException e)
            {
                Console.WriteLine($"Device at {ipAddress} is not responding to SNMP Calls:\n{e.Message}");
                return new UPS(hostname: "Unknown",
                                        ip: ipAddress,
                                        model: "Unknown",
                                        serialNumber: "Unknown",
                                        state: UPS.DeviceState.OffLine);
            }
            //An ImproperOidsException means we are asking for Object IDentifiers the target is unaware of.
            catch (ImproperOidsException e)
            {
                Console.WriteLine($"Device at {ipAddress} is not responding to the configured OIDs:\n{e.Message}");
                return null;
            }

            if (!temperatureResult.Equals(new KeyValuePair<string, string>("", "")))
            {
                return new UPS(hostname:snmpResults["1.3.6.1.4.1.318.1.1.1.1.1.2.0"],
                                        ip:ipAddress,
                                        model:snmpResults["1.3.6.1.4.1.318.1.1.1.1.1.1.0"],
                                        serialNumber:snmpResults["1.3.6.1.4.1.318.1.1.1.1.2.3.0"],
                                        tempSensorOid:UPS.TemperatureProbeOids[tProbeOidIndex],
                                        state:UPS.DeviceState.OnLine,
                                        temperature:float.Parse(temperatureResult.Value));
            } 
            else
            {
                return new UPS(hostname: snmpResults["1.3.6.1.4.1.318.1.1.1.1.1.2.0"],
                                        ip: ipAddress,
                                        model: snmpResults["1.3.6.1.4.1.318.1.1.1.1.1.1.0"],
                                        serialNumber: snmpResults["1.3.6.1.4.1.318.1.1.1.1.2.3.0"],
                                        state: UPS.DeviceState.OnLine);
            }

        }

        private Dictionary<string,string> GetSnmpInfo(string ipAddress, IEnumerable<string> oids)
        {
            //results will be <OID,SNMP Call Result>
            Dictionary<string,string> results = new Dictionary<string, string>();
            /********** Heavily Modified code from the SnmpSharpNet webpage **********/
            /**********      http://www.snmpsharpnet.com/?page_id=111       **********/

            // SNMP community name
            OctetString community = new OctetString("public");

            // Define agent parameters class
            AgentParameters param = new AgentParameters(community);

            // Set SNMP version to 1
            param.Version = SnmpVersion.Ver1;

            // Construct the agent address object
            IpAddress agent = new IpAddress(ipAddress);

            // Construct target
            UdpTarget target = new UdpTarget((IPAddress)agent, 161, 2000, 1);

            // Pdu class used for all requests
            Pdu pdu = new Pdu(PduType.Get);

            //Add list of all relevant OIDs
            foreach (string key in oids)
            {
                pdu.VbList.Add(key);
            }

            // Make SNMP request
            SnmpV1Packet result = (SnmpV1Packet)target.Request(pdu, param);

            // If result is null then agent didn't reply or we couldn't parse the reply.
            if (result != null)
            {
                // ErrorStatus other then 0 is an error returned by 
                // the Agent - see SnmpConstants for error definitions
                if (result.Pdu.ErrorStatus != 0)
                {
                    //Close the connection since we're going to throw an exception.
                    target.Close();

                    // agent reported an error with the request
                    string msg = $"OID {oids.ElementAt(result.Pdu.ErrorIndex)} "
                                 + "has exited with error status "
                                 + $"{ result.Pdu.ErrorStatus}: {Enum.GetName(typeof(PduErrorStatus), result.Pdu.ErrorStatus)}";
                    throw new ImproperOidsException(msg);
                }
                else
                {
                    //Print all information we gleaned from inspecting the UPS
                    IEnumerator<Vb> EnumeratedResults = result.Pdu.GetEnumerator();
                    do
                    {
                        if (EnumeratedResults.Current == null)
                            continue;

                        //Console.WriteLine($"{UPS.Oids[EnumeratedResults.Current.Oid.ToString()],30}: {EnumeratedResults.Current.Value.ToString(),-30}");
                        results.Add(EnumeratedResults.Current.Oid.ToString(),EnumeratedResults.Current.Value.ToString());

                    } while (EnumeratedResults.MoveNext());

                }
            }
            else
            {
                Console.WriteLine("No response received from SNMP agent.");
            }
            target.Close();
            
            return results;

            /******** END Credited Code ********/
        }

        private KeyValuePair<string, string> GetSnmpResultForOid(string ipAddress, string oid)
        {
            //results will be <OID,SNMP Call Result>
            Dictionary<string, string> results = new Dictionary<string, string>();
            /********** Heavily Modified code from the SnmpSharpNet webpage **********/
            /**********      http://www.snmpsharpnet.com/?page_id=111       **********/

            // SNMP community name
            OctetString community = new OctetString("public");

            // Define agent parameters class
            AgentParameters param = new AgentParameters(community);

            // Set SNMP version to 1
            param.Version = SnmpVersion.Ver1;

            // Construct the agent address object
            IpAddress agent = new IpAddress(ipAddress);

            // Construct target
            UdpTarget target = new UdpTarget((IPAddress)agent, 161, 2000, 1);

            // Pdu class used for all requests
            Pdu pdu = new Pdu(PduType.Get);

            pdu.VbList.Add(oid);

            // Make SNMP request
            SnmpV1Packet result = (SnmpV1Packet)target.Request(pdu, param);

            // If result is null then agent didn't reply or we couldn't parse the reply.
            if (result != null)
            {
                // ErrorStatus other then 0 is an error returned by 
                // the Agent - see SnmpConstants for error definitions
                if (result.Pdu.ErrorStatus != 0)
                {
                    //Close the connection since we're going to throw an exception.
                    target.Close();

                    // agent reported an error with the request
                    string msg = $"OID {oid} "
                                 + "has exited with error status "
                                 + $"{ result.Pdu.ErrorStatus}: {Enum.GetName(typeof(PduErrorStatus), result.Pdu.ErrorStatus)}";
                    throw new ImproperOidsException(msg);
                }
                else
                {
                    results.Add(oid, result.Pdu.VbList[0].Value.ToString());
                }
            }
            else
            {
                Console.WriteLine("No response received from SNMP agent.");
            }
            target.Close();

            return results.Count > 0 ? new KeyValuePair<string, string>(oid,results[oid].ToString()) : new KeyValuePair<string, string>("", "");


            /******** END Credited Code ********/
        }


        static void TestDriver()
        {
            Model AtwaterMonitorModel = new Model();
            float epsilon = 0.0000001f;
            UPS n = new UPS("UPS_01", @"10.10.41.41");

            n.AddAmbientTemperatureReading(89f, DateTime.Now);
            n.AddAmbientTemperatureReading(95f, DateTime.Now);
            n.AddAmbientTemperatureReading(87f, DateTime.Now);
            n.AddAmbientTemperatureReading(78f, DateTime.Now);
            AtwaterMonitorModel.AddNetworkDevice(n);

            UPS n2 = new UPS("UPS_03", @"10.10.102.41");

            n2.AddAmbientTemperatureReading(89f, DateTime.Now);
            n2.AddAmbientTemperatureReading(95f, DateTime.Now);
            n2.AddAmbientTemperatureReading(87f, DateTime.Now);
            n2.AddAmbientTemperatureReading(87.8f, DateTime.Now);

            AtwaterMonitorModel.AddNetworkDevice(n2);

            //Tests for the UPS and NetworkDevice Classes
            Debug.Assert(n.Hostname == "UPS_01", "FAILED: UPS.Constructor/Getter - Hostname");
            Debug.Assert(n.IPAddress == @"10.10.41.41", "FAILED: UPS.Constructor/Getter - IPAddress");
            Debug.Assert(n.CurrentAmbientTemperature - 78f < epsilon, "Failed: UPS.AddTemperatureReading/Current Temperature Getter");
            Debug.Assert(n.AverageAmbientTemperature - (89f + 95f + 87f + 78f) / 4f < epsilon, "FAILED: UPS.AverageTemperature/UPS.CalculateAverageTemperature/Getter");

            //Tests for the Model Class
            Debug.Assert(n2 == AtwaterMonitorModel.GetDeviceWithHostname("UPS_03"),"FAILED: Model.GetDevicesWithHostname()");
            Debug.Assert(n == AtwaterMonitorModel.GetDeviceWithIP("10.10.41.41"), "FAILED: Model.GetDeviceWithIP()");
            Debug.Assert(n2 == AtwaterMonitorModel.GetDevicesWithStatus(NetworkDevice.DeviceState.OffLine)[1]
                && n == AtwaterMonitorModel.GetDevicesWithStatus(NetworkDevice.DeviceState.OffLine)[0], "FAILED: Model.GetDevicesWithStatus()");
            Debug.Assert(n2 == AtwaterMonitorModel.GetDevicesAboveTemperature(79f)[0], "FAILED: Model.GetDevicesAboveTemperature()");

        }
    }
}
