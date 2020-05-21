﻿using System;

#if DOUBLE
using Real = System.Double;
#elif NETSTANDARD2_1
using Real = System.Single;
using Math = System.MathF;
#elif NETSTANDARD2_0
using Real = System.Single;
using Math = KelpNet.MathF;
#endif

namespace KelpNet
{
#if !DOUBLE
    public class AmsBound<T> : Adam<T> where T : unmanaged, IComparable<T>
    {
        public T InitialAlpha;

        public T Upper;
        public T Lower;

        public T FinalLr;
        public T Gamma;

        public AmsBound(T? alpha = null, T? beta1 = null, T? beta2 = null, T? finalLr = null, T? gamma = null, T? epsilon = null, T? eta = null) : base(alpha: alpha, beta1: beta1, beta2: beta2, epsilon: epsilon, eta: eta)
        {
            this.InitialAlpha = alpha ?? (TVal<T>)0.001;
            this.FinalLr = finalLr ?? (TVal<T>)0.1;
            this.Gamma = gamma ?? (TVal<T>)1e-3;

            switch (this)
            {
                case AmsBound<float> amsBoundF:
                    amsBoundF.Update = () => OptimizerF.Update(amsBoundF);
                    break;

                case AmsBound<double> amsBoundD:
                    amsBoundD.Update = () => OptimizerD.Update(amsBoundD);
                    break;
            }
        }

        public override void AddFunctionParameters(NdArray<T>[] functionParameters)
        {
            foreach (NdArray<T> functionParameter in functionParameters)
            {
                this.OptimizerParameters.Add(new AmsBoundParameter<T>(functionParameter, this));
            }
        }

        public T Clip(T val)
        {
            if (val.CompareTo(Lower) <= 0) return Lower;
            if (val.CompareTo(Upper) >= 0) return Upper;
            return val;
        }
    }
#endif

    //外部公開しないため型スイッチを必要としない
    internal static class AmsBound
    {
        public static void UpdateBound(Real Alpha, Real InitialAlpha, Real Gamma, long UpdateCount, ref Real FinalLr, out Real Lower, out Real Upper)
        {
            FinalLr = FinalLr * Alpha / InitialAlpha;

            Lower = FinalLr * (1.0f - 1.0f / (Gamma * UpdateCount + 1.0f));
            Upper = FinalLr * (1.0f + 1.0f / (Gamma * UpdateCount));
        }
    }

#if !DOUBLE
    public class AmsBoundParameter<T> : OptimizerParameter<T> where T : unmanaged, IComparable<T>
    {
        private readonly AmsBound<T> _optimizer;

        private readonly T[] m;
        private readonly T[] v;
        private readonly T[] vhat;

        public AmsBoundParameter(NdArray<T> parameter, AmsBound<T> optimizer) : base(parameter)
        {
            this.m = new T[parameter.Data.Length];
            this.v = new T[parameter.Data.Length];
            this.vhat = new T[parameter.Data.Length];

            this._optimizer = optimizer;

            switch (this)
            {
                case AmsBoundParameter<float> amsBoundParameterF:
                    amsBoundParameterF.UpdateFunctionParameters = () => AmsBoundParameterF.UpdateFunctionParameters(amsBoundParameterF._optimizer.Alpha, amsBoundParameterF._optimizer.InitialAlpha, amsBoundParameterF._optimizer.Gamma, amsBoundParameterF._optimizer.Beta1, amsBoundParameterF._optimizer.Beta2, amsBoundParameterF._optimizer.Epsilon, amsBoundParameterF._optimizer.Eta, _optimizer.UpdateCount, amsBoundParameterF.FunctionParameter, amsBoundParameterF.m, amsBoundParameterF.v, amsBoundParameterF.vhat, ref amsBoundParameterF._optimizer.FinalLr, out amsBoundParameterF._optimizer.Lower, out amsBoundParameterF._optimizer.Upper, amsBoundParameterF._optimizer.Clip);
                    break;

                case AmsBoundParameter<double> amsBoundParameterD:
                    amsBoundParameterD.UpdateFunctionParameters = () => AmsBoundParameterD.UpdateFunctionParameters(amsBoundParameterD._optimizer.Alpha, amsBoundParameterD._optimizer.InitialAlpha, amsBoundParameterD._optimizer.Gamma, amsBoundParameterD._optimizer.Beta1, amsBoundParameterD._optimizer.Beta2, amsBoundParameterD._optimizer.Epsilon, amsBoundParameterD._optimizer.Eta, _optimizer.UpdateCount, amsBoundParameterD.FunctionParameter, amsBoundParameterD.m, amsBoundParameterD.v, amsBoundParameterD.vhat, ref amsBoundParameterD._optimizer.FinalLr, out amsBoundParameterD._optimizer.Lower, out amsBoundParameterD._optimizer.Upper, amsBoundParameterD._optimizer.Clip);
                    break;
            }
        }
    }
#endif

#if DOUBLE
    public static class AmsBoundParameterD
#else
    public static class AmsBoundParameterF
#endif
    {
        public static void UpdateFunctionParameters(Real alpha, Real initialAlpha, Real gamma, Real beta1, Real beta2, Real epsilon, Real eta, long updateCount, NdArray<Real> functionParameter, Real[] m, Real[] v, Real[] vhat, ref Real finalLr, out Real lower, out Real upper, Func<Real, Real> clip)
        {
            Real alphaT = AdamParameter.GetAlphaT(alpha, beta1, beta2, updateCount);

            AmsBound.UpdateBound(alpha, initialAlpha, gamma, updateCount, ref finalLr, out lower, out upper);

            for (int i = 0; i < functionParameter.Data.Length; i++)
            {
                Real grad = functionParameter.Grad[i];

                m[i] += (1 - beta1) * (grad - m[i]);
                v[i] += (1 - beta2) * (grad * grad - v[i]);

                if (vhat[i] < v[i])
                {
                    vhat[i] = v[i];
                }

                Real step = clip(alphaT / (Math.Sqrt(vhat[i]) + epsilon));

                functionParameter.Data[i] -= eta * step * m[i];
            }
        }
    }
}
