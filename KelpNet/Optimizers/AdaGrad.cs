﻿using System;
using KelpNet.Common;
#if !DEBUG
using System.Threading.Tasks;
#endif

namespace KelpNet.Optimizers
{
    public class AdaGrad : Optimizer
    {
        private NdArray[] h;

        private double lr;
        private double eps;

        public AdaGrad(double lr = 0.01, double eps = 1e-8)
        {
            this.lr = lr;
            this.eps = eps;
        }

        protected override void DoUpdate()
        {
#if DEBUG
            for (int i = 0; i < Parameters.Count; i++)
#else
            Parallel.For(0, Parameters.Count, i => 
#endif
            {
                for (int k = 0; k < Parameters[i].Length; k++)
                {
                    var grad = Parameters[i].Grad.Data[k];

                    this.h[i].Data[k] += grad * grad;

                    Parameters[i].Param.Data[k] -= this.lr * grad / (Math.Sqrt(this.h[i].Data[k]) + this.eps);
                }
            }
#if !DEBUG
            );
#endif
        }

        protected override void Initialize()
        {
            this.h = new NdArray[Parameters.Count];

            for (int i = 0; i < this.h.Length; i++)
            {
                this.h[i] = NdArray.ZerosLike(Parameters[i].Param);
            }
        }
    }
}
