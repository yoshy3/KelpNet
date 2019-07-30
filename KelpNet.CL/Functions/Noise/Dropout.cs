﻿using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.Serialization;
using Cloo;
using KelpNet.CL.Properties;

namespace KelpNet.CL
{
    [DataContract(Name = "Dropout")]
    public class Dropout : SelectableSingleInputFunction, IParallelizable
    {
        const string FUNCTION_NAME = "Dropout";

        [DataMember]
        public Real DropoutRatio;

        [DataMember]
        private readonly List<Real[]> maskStack = new List<Real[]>();


        //[NonSerialized]
        [DebuggerBrowsable(DebuggerBrowsableState.Never)]
        public ComputeKernel ForwardKernel;

        //[NonSerialized]
        [DebuggerBrowsable(DebuggerBrowsableState.Never)]
        public ComputeKernel BackwardKernel;


        [DataMember]
        public bool IsParallel { get; set; }

        public Dropout(double dropoutRatio = 0.5, string name = FUNCTION_NAME, string[] inputNames = null, string[] outputNames = null, bool gpuEnable = false) : base(name, inputNames, outputNames)
        {
            this.DropoutRatio = dropoutRatio;

            this.SetParallel(gpuEnable);
        }

        public bool SetParallel(bool enable)
        {
            this.IsParallel = enable & OpenCL.Enable;

            if (IsParallel)
            {
                InitParallel();

                SingleInputForward = ForwardGpu;
                SingleOutputBackward = BackwardGpu;
            }
            else
            {
                SingleInputForward = ForwardCpu;
                SingleOutputBackward = BackwardCpu;
            }

            return IsParallel;
        }

        public void InitParallel()
        {
            if (IsParallel)
            {
                string kernelSource = OpenCL.GetKernelSource(Resources.Dropout);
                ComputeProgram program = OpenCL.CreateProgram(kernelSource);

                ForwardKernel = program.CreateKernel("DropoutForward");
                BackwardKernel = program.CreateKernel("DropoutBackward");
            }
        }

        private Real[] MakeMask(int xLength)
        {
            Real[] mask = new Real[xLength];
            Real scale = 1 / (1 - this.DropoutRatio);

            for (int i = 0; i < mask.Length; i++)
            {
                mask[i] = Mother.Dice.NextDouble() >= this.DropoutRatio ? scale : 0;
            }

            this.maskStack.Add(mask);

            return mask;
        }

        public NdArray ForwardCpu(NdArray x)
        {
            Real[] result = new Real[x.Data.Length];
            Real[] mask = MakeMask(x.Length);

            for (int i = 0; i < x.Data.Length; i++)
            {
                result[i] = x.Data[i] * mask[i % mask.Length];
            }

            return NdArray.Convert(result, x.Shape, x.BatchCount, this);
        }

        public NdArray ForwardGpu(NdArray x)
        {
            Real[] result = new Real[x.Data.Length];
            Real[] mask = MakeMask(x.Length);

            using (ComputeBuffer<Real> gpuX = new ComputeBuffer<Real>(OpenCL.Context, ComputeMemoryFlags.ReadOnly | ComputeMemoryFlags.UseHostPointer, x.Data))
            using (ComputeBuffer<Real> gpuMask = new ComputeBuffer<Real>(OpenCL.Context, ComputeMemoryFlags.ReadOnly | ComputeMemoryFlags.UseHostPointer, mask))
            using (ComputeBuffer<Real> gpuY = new ComputeBuffer<Real>(OpenCL.Context, ComputeMemoryFlags.WriteOnly | ComputeMemoryFlags.AllocateHostPointer, result.Length))
            {
                ForwardKernel.SetMemoryArgument(0, gpuX);
                ForwardKernel.SetMemoryArgument(1, gpuMask);
                ForwardKernel.SetMemoryArgument(2, gpuY);
                ForwardKernel.SetValueArgument(3, mask.Length);

                OpenCL.CommandQueue.Execute
                (
                    ForwardKernel,
                    null,
                    new long[] { x.Data.Length },
                    null,
                    null
                );

                OpenCL.CommandQueue.Finish();
                OpenCL.CommandQueue.ReadFromBuffer(gpuY, ref result, true, null);
            }

            return NdArray.Convert(result, x.Shape, x.BatchCount, this);
        }

        public void BackwardCpu(NdArray y, NdArray x)
        {
            Real[] result = y.Grad.ToArray();
            Real[] mask = this.maskStack[this.maskStack.Count - 1];
            this.maskStack.RemoveAt(this.maskStack.Count - 1);

            for (int b = 0; b < y.BatchCount; b++)
            {
                for (int i = 0; i < mask.Length; i++)
                {
                    result[b * y.Length + i] *= mask[i];
                }
            }

            for (int i = 0; i < x.Grad.Length; i++)
            {
                x.Grad[i] += result[i];
            }
        }

        public void BackwardGpu(NdArray y, NdArray x)
        {
            Real[] result = y.Grad.ToArray();
            Real[] mask = this.maskStack[this.maskStack.Count - 1];
            this.maskStack.RemoveAt(this.maskStack.Count - 1);

            using (ComputeBuffer<Real> gpuMask = new ComputeBuffer<Real>(OpenCL.Context, ComputeMemoryFlags.ReadOnly | ComputeMemoryFlags.UseHostPointer, mask))
            using (ComputeBuffer<Real> gpugX = new ComputeBuffer<Real>(OpenCL.Context, ComputeMemoryFlags.ReadWrite | ComputeMemoryFlags.UseHostPointer, result))
            {
                BackwardKernel.SetMemoryArgument(0, gpuMask);
                BackwardKernel.SetMemoryArgument(1, gpugX);
                BackwardKernel.SetValueArgument(2, y.Length);

                OpenCL.CommandQueue.Execute
                (
                    BackwardKernel,
                    null,
                    new long[] { mask.Length, y.BatchCount },
                    null,
                    null
                );

                OpenCL.CommandQueue.Finish();
                OpenCL.CommandQueue.ReadFromBuffer(gpugX, ref result, true, null);
            }

            for (int i = 0; i < x.Grad.Length; i++)
            {
                x.Grad[i] += result[i];
            }
        }


        //Predict時に何もしない
        public override NdArray Predict(NdArray input)
        {
            return input;
        }
    }
}
