﻿using System;
using System.Diagnostics;
using System.Runtime.Serialization;
using KelpNet.CL.Common;

#if DOUBLE
using Real = System.Double;
using LinearFunc = KelpNet.CPU.LinearD;
#else
using KelpNet.CL.Properties;
using Real = System.Single;
using LinearFunc = KelpNet.CPU.LinearF;
#endif

namespace KelpNet.CL
{
#if !DOUBLE
    [DataContract(Name = "Linear", Namespace = "KelpNet")]
    public class Linear<T> : CPU.Linear<T>, ICompressibleFunction<T> where T : unmanaged, IComparable<T>
    {
        public string FunctionName => "Linear";
        public string KernelSource { get; set; } = OpenCL.GetKernelSource(Resources.Linear);

        [DebuggerBrowsable(DebuggerBrowsableState.Never)]
        public ComputeKernel ForwardKernel { get; set; }

        [DebuggerBrowsable(DebuggerBrowsableState.Never)]
        public ComputeKernel BackwardgWKernel { get; set; }

        [DebuggerBrowsable(DebuggerBrowsableState.Never)]
        public ComputeKernel BackwardgXKernel { get; set; }

        [DataMember]
        public string ForwardKernelName { get; set; }

        [DataMember]
        public string BackwardgWKernelName { get; set; }

        [DataMember]
        public string BackwardgXKernelName { get; set; }

        [DataMember]
        public bool IsParallel { get; set; }

        public bool SetParallel(bool enable)
        {
            bool result = this.SetParallel<T>(enable);
            this.InitFunc(new StreamingContext());
            return result;
        }

        public Linear(int inputCount, int outputCount, bool noBias = false, Array initialW = null, T[] initialb = null, ICompressibleActivation<T> activation = null, string name = "Linear", string[] inputNames = null, string[] outputNames = null, bool gpuEnable = false) : base(inputCount, outputCount, noBias, initialW, initialb, activation, name, inputNames, outputNames)
        {
            this.SetParallel(gpuEnable);
            this.InitFunc(new StreamingContext());
        }

        public Linear(CPU.Linear<T> linear) : base(linear.Name, linear.InputNames, linear.OutputNames)
        {
            this.Weight = linear.Weight;
            this.Bias = linear.Bias;

            this.Parameters = linear.Parameters;

            this.Activation = (ICompressibleActivation<T>)CLConverter.Convert(linear.Activation);

            this.SetParallel(true);
            this.InitFunc(new StreamingContext());
        }

        [OnDeserializing]
        public void InitFunc(StreamingContext sc)
        {
            if (IsParallel)
            {
                switch (this)
                {
                    case Linear<float> linearF:
                        linearF.SingleInputForward = x => LinearF.SingleInputForward(x, linearF.Weight, linearF.Bias, linearF.ForwardKernel, linearF);
                        linearF.SingleOutputBackward = (y, x) => LinearF.SingleOutputBackward(y, x, linearF.Weight, linearF.Bias, linearF.BackwardgWKernel, linearF.BackwardgXKernel, linearF.Activation);
                        break;

                    case Linear<double> linearD:
                        linearD.SingleInputForward = x => LinearD.SingleInputForward(x, linearD.Weight, linearD.Bias, linearD.ForwardKernel, linearD);
                        linearD.SingleOutputBackward = (y, x) => LinearD.SingleOutputBackward(y, x, linearD.Weight, linearD.Bias, linearD.BackwardgWKernel, linearD.BackwardgXKernel, linearD.Activation);
                        break;
                }
            }
        }

        public override CPU.Convolution2D<T> AsConvolution2D()
        {
            return new Convolution2D<T>(this);
        }
    }

    public partial class Function
    {
        public static NdArray<float> Linear(NdArray<float> x, NdArray<float> weight, NdArray<float> bias, ComputeKernel forwardKernel, ICompressibleActivation<float> activation = null)
        {
            return LinearF.SingleInputForward(x, weight, bias, forwardKernel, new Linear<float>(weight.Shape[0], weight.Shape[1], bias != null, weight.Data, bias?.Data, activation));
        }

        public static NdArray<double> Linear(NdArray<double> x, NdArray<double> weight, NdArray<double> bias, ComputeKernel forwardKernel, ICompressibleActivation<double> activation = null)
        {
            return LinearD.SingleInputForward(x, weight, bias, forwardKernel, new Linear<double>(weight.Shape[0], weight.Shape[1], bias != null, weight.Data, bias?.Data, activation));
        }
    }
#endif

#if DOUBLE
    public static class LinearD
#else
    public static class LinearF
#endif
    {
        public static NdArray<Real> SingleInputForward(NdArray<Real> x, NdArray<Real> weight, NdArray<Real> bias, ComputeKernel forwardKernel, IFunction<Real> linear)
        {
            int outputCount = weight.Shape[0];
            int inputCount = weight.Shape[1];

            Real[] y = bias == null ? new Real[outputCount * x.BatchCount] : LinearFunc.GetBiasedValue(x.BatchCount, outputCount, bias.Data);

            using (ComputeBuffer<Real> gpuX = new ComputeBuffer<Real>(OpenCL.Context, ComputeMemoryFlags.ReadOnly | ComputeMemoryFlags.UseHostPointer, x.Data))
            using (ComputeBuffer<Real> gpuW = new ComputeBuffer<Real>(OpenCL.Context, ComputeMemoryFlags.ReadOnly | ComputeMemoryFlags.UseHostPointer, weight.Data))
            using (ComputeBuffer<Real> gpuY = new ComputeBuffer<Real>(OpenCL.Context, ComputeMemoryFlags.ReadWrite | ComputeMemoryFlags.UseHostPointer, y))
            {
                forwardKernel.SetMemoryArgument(0, gpuX);
                forwardKernel.SetMemoryArgument(1, gpuW);
                forwardKernel.SetMemoryArgument(2, gpuY);
                forwardKernel.SetValueArgument(3, outputCount);
                forwardKernel.SetValueArgument(4, inputCount);

                OpenCL.CommandQueue.Execute
                    (
                        forwardKernel,
                        null,
                        new long[] { outputCount, x.BatchCount },
                        null,
                        null
                    );

                OpenCL.CommandQueue.Finish();
                OpenCL.CommandQueue.ReadFromBuffer(gpuY, ref y, true, null);
            }

            return NdArray.Convert(y, new[] { outputCount }, x.BatchCount, linear);
        }

        public static void SingleOutputBackward(NdArray<Real> y, NdArray<Real> x, NdArray<Real> weight, NdArray<Real> bias, ComputeKernel backwardgWKernel, ComputeKernel backwardgXKernel, KelpNet.ICompressibleActivation<Real> activation)
        {
            int outputCount = weight.Shape[0];
            int inputCount = weight.Shape[1];

            Real[] gx = new Real[x.Data.Length];
            Real[] activatedgy = activation != null ? activation.GetActivatedgy(y) : y.Grad;
            if (bias != null)
            {
                LinearFunc.CalcBiasGrad(activatedgy, y.BatchCount, outputCount, bias.Grad);
            }

            using (ComputeBuffer<Real> gpugY = new ComputeBuffer<Real>(OpenCL.Context, ComputeMemoryFlags.ReadOnly | ComputeMemoryFlags.UseHostPointer, activatedgy))
            {
                using (ComputeBuffer<Real> gpugW = new ComputeBuffer<Real>(OpenCL.Context, ComputeMemoryFlags.ReadWrite | ComputeMemoryFlags.UseHostPointer, weight.Grad))
                using (ComputeBuffer<Real> gpuX = new ComputeBuffer<Real>(OpenCL.Context, ComputeMemoryFlags.ReadOnly | ComputeMemoryFlags.UseHostPointer, x.Data))
                {
                    backwardgWKernel.SetMemoryArgument(0, gpugY);
                    backwardgWKernel.SetMemoryArgument(1, gpuX);
                    backwardgWKernel.SetMemoryArgument(2, gpugW);
                    backwardgWKernel.SetValueArgument(3, y.BatchCount);
                    backwardgWKernel.SetValueArgument(4, outputCount);
                    backwardgWKernel.SetValueArgument(5, inputCount);

                    OpenCL.CommandQueue.Execute
                    (
                        backwardgWKernel,
                        null,
                        new long[] { inputCount, outputCount },
                        null,
                        null
                    );

                    OpenCL.CommandQueue.Finish();
                    OpenCL.CommandQueue.ReadFromBuffer(gpugW, ref weight.Grad, true, null);
                }

                using (ComputeBuffer<Real> gpugX = new ComputeBuffer<Real>(OpenCL.Context, ComputeMemoryFlags.WriteOnly | ComputeMemoryFlags.AllocateHostPointer, gx.Length))
                using (ComputeBuffer<Real> gpuW = new ComputeBuffer<Real>(OpenCL.Context, ComputeMemoryFlags.ReadOnly | ComputeMemoryFlags.UseHostPointer, weight.Data))
                {
                    backwardgXKernel.SetMemoryArgument(0, gpugY);
                    backwardgXKernel.SetMemoryArgument(1, gpuW);
                    backwardgXKernel.SetMemoryArgument(2, gpugX);
                    backwardgXKernel.SetValueArgument(3, y.BatchCount);
                    backwardgXKernel.SetValueArgument(4, outputCount);
                    backwardgXKernel.SetValueArgument(5, inputCount);

                    OpenCL.CommandQueue.Execute
                    (
                        backwardgXKernel,
                        null,
                        new long[] { inputCount, y.BatchCount },
                        null,
                        null
                    );

                    OpenCL.CommandQueue.Finish();
                    OpenCL.CommandQueue.ReadFromBuffer(gpugX, ref gx, true, null);
                }
            }

            for (int i = 0; i < x.Grad.Length; i++)
            {
                x.Grad[i] += gx[i];
            }
        }
    }
}
