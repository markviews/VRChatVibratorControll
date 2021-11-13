﻿using MelonLoader;
using Buttplug;
using System.Collections.Generic;
using System;
using System.Linq;

namespace Vibrator_Controller {
    public enum Hand {
        none, shared, left, right, both, either
    }
    public class Toy {
        internal static Dictionary<ulong, Toy> remoteToys { get; set; } = new Dictionary<ulong, Toy>();
        internal static Dictionary<ulong, Toy> myToys { get; set; } = new Dictionary<ulong, Toy>();

        internal static List<Toy> allToys => remoteToys.Select(x=>x.Value).Union(myToys.Select(x => x.Value)).ToList();

        internal Hand hand = Hand.none;
        internal string name;
        internal ulong id;
        internal bool isActive = true;

        internal ButtplugClientDevice device;
        internal int lastSpeed = 0, lastEdgeSpeed = 0, lastContraction = 0;

        internal bool supportsRotate = false, supportsLinear = false, supportsTwoVibrators = false, supportsBatteryLVL = false;
        internal int maxSpeed = 20, maxSpeed2 = -1, maxLinear = -1;
        internal double battery = -1;

        internal Toy(ButtplugClientDevice device) {
            id = device.Index;
            hand = Hand.shared;
            name = device.Name;
            this.device = device;

            //remove company name
            if (name.Split(' ').Length > 1) name = name.Split(' ')[1];

            if (myToys.ContainsKey(id))
            {
                MelonLogger.Msg("Device reconnected: " + name + " [" + id + "]");
                myToys[id].name = name; //id should be uniquie but just to be sure
                myToys[id].device = device;
                myToys[id].enable();
                return;
            }



            MelonLogger.Msg("Device connected: " + name + " [" + id + "]");

            if (device.AllowedMessages.ContainsKey(ServerMessage.Types.MessageAttributeType.LinearCmd))
                supportsLinear = true;

            if (device.AllowedMessages.ContainsKey(ServerMessage.Types.MessageAttributeType.RotateCmd))
                supportsRotate = true; 
            

            if (device.AllowedMessages.ContainsKey(ServerMessage.Types.MessageAttributeType.BatteryLevelCmd)) {
                supportsBatteryLVL = true;
                device.SendBatteryLevelCmd().ContinueWith(battery => { 
                    this.battery = battery.Result;
                });
            }

            //prints info about the device
            foreach (KeyValuePair<ServerMessage.Types.MessageAttributeType, ButtplugMessageAttributes> entry in device.AllowedMessages)
                MelonLogger.Msg("[" + id + "] Allowed Message: " + entry.Key);

            if (device.AllowedMessages.ContainsKey(ServerMessage.Types.MessageAttributeType.VibrateCmd)) {
                ButtplugMessageAttributes attributes = device.AllowedMessages[ServerMessage.Types.MessageAttributeType.VibrateCmd];

                if (attributes.ActuatorType != null && attributes.ActuatorType.Length > 0)
                    MelonLogger.Msg("[" + id +  "] ActuatorType " + string.Join(", ", attributes.ActuatorType));

                if (attributes.StepCount != null && attributes.StepCount.Length > 0) {
                    MelonLogger.Msg("[" + id + "] StepCount " + string.Join(", ", attributes.StepCount));
                    maxSpeed = (int)attributes.StepCount[0];
                }
                if (attributes.StepCount != null && attributes.StepCount.Length == 2)
                {
                    supportsTwoVibrators = true;
                    maxSpeed2 = (int)attributes.StepCount[1];
                }

                if (attributes.Endpoints != null && attributes.Endpoints.Length > 0)
                    MelonLogger.Msg("[" + id + "] Endpoints " + string.Join(", ", attributes.Endpoints));

                if (attributes.MaxDuration != null && attributes.MaxDuration.Length > 0)
                    MelonLogger.Msg("[" + id + "] MaxDuration " + string.Join(", ", attributes.MaxDuration));

                if (attributes.Patterns != null && attributes.Patterns.Length > 0)
                    foreach (string[] pattern in attributes.Patterns)
                        MelonLogger.Msg("[" + id + "] Pattern " + string.Join(", ", pattern));
            }

            myToys.Add(id, this);
        }

        internal Toy(string name, ulong id, int maxSpeed, int maxSpeed2, int maxLinear, bool supportsRotate) {

            if (remoteToys.ContainsKey(id))
            {
                MelonLogger.Msg("Device reconnected: " + name + " [" + id + "]");
                if (maxSpeed2 != -1) remoteToys[id].supportsTwoVibrators = true;
                if (maxLinear != -1) remoteToys[id].supportsLinear = true;
                remoteToys[id].name = name;
                remoteToys[id].supportsRotate = supportsRotate;
                remoteToys[id].maxSpeed = maxSpeed;
                remoteToys[id].maxSpeed2 = maxSpeed2;
                remoteToys[id].maxLinear = maxLinear;
                remoteToys[id].enable();
                MelonLogger.Msg($"Reconnected toy Name: {remoteToys[id].name}, ID: {remoteToys[id].id} Max Speed: {remoteToys[id].maxSpeed}" + (remoteToys[id].supportsTwoVibrators ? $", Max Speed 2: {remoteToys[id].maxSpeed2}" : "") + (remoteToys[id].supportsLinear ? $", Max Linear Speed: {remoteToys[id].maxLinear}" : "") + (remoteToys[id].supportsRotate ? $", Supports Rotation" : ""));
                return;
            }

            if (maxSpeed2 != -1) supportsTwoVibrators = true;
            if (maxLinear != -1) supportsLinear = true;

            this.supportsRotate = supportsRotate;
            this.maxSpeed = maxSpeed;
            this.maxSpeed2 = maxSpeed2;
            this.maxLinear = maxLinear;
            this.name = name;
            this.id = id;
            
            MelonLogger.Msg($"Added toy Name: {name}, ID: {id} Max Speed: {maxSpeed}" + (supportsTwoVibrators ? $", Max Speed 2: {maxSpeed2}" : "") + (supportsLinear ? $", Max Linear Speed: {maxLinear}" : "") + (supportsRotate ? $", Supports Rotation" : ""));

            remoteToys.Add(id, this);
        }

        internal void disable() {
            if (isActive) {
                isActive = false;
                MelonLogger.Msg("Disabled toy: " + name);
                hand = Hand.none;

                if (isLocal()) {
                    VRCWSIntegration.SendMessage(new VibratorControllerMessage(Commands.RemoveToy, this));
                }
                    
            }
        }

        internal void enable() {
            if (!isActive) {
                isActive = true;
                MelonLogger.Msg("Enabled toy: " + name);
            }
        }

        internal void setSpeed(int speed) {
            if (speed != lastSpeed) {
                lastSpeed = speed;
                if (isLocal()) {
                    try
                    {
                        if(supportsTwoVibrators)
                            device.SendVibrateCmd(new List<double> { (double)lastSpeed / maxSpeed, (double)lastEdgeSpeed / maxSpeed2 });
                        else
                            device.SendVibrateCmd((double)speed / maxSpeed);

                        //MelonLogger.Msg("set device speed to " + ((double)speed / maxSpeed));
                    } catch (ButtplugDeviceException) {
                        MelonLogger.Error("Toy not connected");
                    }
                } else {
                    VRCWSIntegration.SendMessage(new VibratorControllerMessage(Commands.SetSpeed, this, speed));
                }

            }
        }

        internal void setEdgeSpeed(int speed) {
            if (speed != lastEdgeSpeed) {
                lastEdgeSpeed = speed;

                if (isLocal()) {
                    try {
                        device.SendVibrateCmd(new List<double> { (double)lastSpeed / maxSpeed, (double)lastEdgeSpeed / maxSpeed2 });
                    } catch (ButtplugDeviceException) {
                        MelonLogger.Error("Toy not connected");
                    }
                } else {
                    VRCWSIntegration.SendMessage(new VibratorControllerMessage(Commands.SetSpeedEdge, this, speed));
                }


            }
        }

        internal void setContraction(int speed) {
            if (lastContraction != speed) {
                lastContraction = speed;

                if (isLocal()) {
                    try {
                        //moves to new position in 1 second
                        device.SendLinearCmd(1000, (double)speed / maxLinear);
                    } catch (ButtplugDeviceException) {
                        MelonLogger.Error("Toy not connected");
                    }
                } else {
                    VRCWSIntegration.SendMessage(new VibratorControllerMessage(Commands.SetAir, this, speed));
                }

            }
        }

        internal bool clockwise = false;
        internal void rotate() {

            if (isLocal()) {
                try {
                    clockwise = !clockwise;
                    device.SendRotateCmd(lastSpeed, clockwise);
                } catch (ButtplugDeviceException) {
                    MelonLogger.Error("Toy not connected");
                }
            } else {
                VRCWSIntegration.SendMessage(new VibratorControllerMessage(Commands.SetRotate, this));
            }
            
        }

        internal void changeHand() {
            if (!isActive) return;

            hand++;
            if (hand > Enum.GetValues(typeof(Hand)).Cast<Hand>().Max())
                hand = 0;

            if (hand == Hand.shared && !isLocal())
                hand++;
            if (hand == Hand.both && !supportsTwoVibrators)
                hand++;

            if (isLocal()) {
                if (hand == Hand.shared) {
                    VRCWSIntegration.SendMessage(new VibratorControllerMessage(Commands.AddToy, this));
                } else {
                    VRCWSIntegration.SendMessage(new VibratorControllerMessage(Commands.RemoveToy, this));
                }
            }
        }

        //returns true if this is a local bluetooth device (controlled by someone else)
        internal bool isLocal() {
            return device != null;
        }

    }
}