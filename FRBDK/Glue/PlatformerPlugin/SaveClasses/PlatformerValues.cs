﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FlatRedBall.PlatformerPlugin.SaveClasses
{
    public class PlatformerValues
    {
        public string Name { get; set; }
        public float MaxSpeedX { get; set; }
        public float AccelerationTimeX { get; set; }
        public float DecelerationTimeX { get; set; }
        public float Gravity { get; set; }
        public float MaxFallSpeed { get; set; }
        public float JumpVelocity { get; set; }
        public float JumpApplyLength { get; set; }
        public bool JumpApplyByButtonHold { get; set; }
        public bool UsesAcceleration { get; set; }
    }
}
