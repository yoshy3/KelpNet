﻿using System;
using KelpNet.Common;
using KelpNet.Common.Functions;
using KelpNet.Common.Optimizers;

namespace KelpNet.Optimizers
{
    [Serializable]
    public class MomentumSGD : Optimizer
    {
        public Real LearningRate;
        public Real Momentum;

        public MomentumSGD(Real? learningRate = null, Real? momentum = null)
        {
            this.LearningRate = learningRate ?? 0.01f;
            this.Momentum = momentum ?? 0.9f;
        }

        internal override void AddFunctionParameters(FunctionParameter[] functionParameters)
        {
            foreach (FunctionParameter functionParameter in functionParameters)
            {
                this.OptimizerParameters.Add(new MomentumSGDParameter(functionParameter, this));
            }
        }
    }

    [Serializable]
    class MomentumSGDParameter : OptimizerParameter
    {
        private readonly MomentumSGD optimiser;
        private readonly Real[] v;

        public MomentumSGDParameter(FunctionParameter functionParameter, MomentumSGD optimiser) : base(functionParameter)
        {
            this.v = new Real[functionParameter.Length];
            this.optimiser = optimiser;
        }

        public override void UpdateFunctionParameters()
        {
            for (int i = 0; i < this.FunctionParameter.Length; i++)
            {
                this.v[i] *= this.optimiser.Momentum;
                this.v[i] -= this.optimiser.LearningRate * this.FunctionParameter.Grad.Data[i];

                this.FunctionParameter.Param.Data[i] += this.v[i];
            }
        }
    }

}
