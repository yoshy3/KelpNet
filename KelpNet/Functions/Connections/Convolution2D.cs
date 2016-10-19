﻿using System;
using KelpNet.Common;
#if !DEBUG
using System.Threading.Tasks;
#endif

namespace KelpNet.Functions.Connections
{
    [Serializable]
    public class Convolution2D : NeedPreviousDataFunction
    {
        public NdArray W;
        public NdArray b;

        public NdArray gW;
        public NdArray gb;

        private int _kSize;
        private int _stride;
        private int _pad;

        public Convolution2D(int inputChannels, int outputChannels, int kSize, int stride = 1, int pad = 0, bool noBias = false, double[,,,] initialW = null, double[] initialb = null, string name = "Conv2D") : base(name)
        {
            this._kSize = kSize;
            this._stride = stride;
            this._pad = pad;

            this.W = NdArray.Zeros(outputChannels, inputChannels, kSize, kSize);
            this.gW = NdArray.ZerosLike(this.W);

            this.b = NdArray.Zeros(outputChannels);

            if (initialW == null)
            {
                InitWeight(this.W);
            }
            else
            {
                //サイズチェックを兼ねる
                Buffer.BlockCopy(initialW, 0, this.W.Data, 0, sizeof(double) * initialW.Length);
            }

            Parameters.Add(new OptimizeParameter(this.W, this.gW, Name + " W"));

            if (!noBias)
            {
                this.gb = NdArray.ZerosLike(this.b);

                if (initialb != null)
                {
                    Buffer.BlockCopy(initialb, 0, this.b.Data, 0, sizeof(double) * initialb.Length);
                }

                Parameters.Add(new OptimizeParameter(this.b, this.gb, Name + " b"));
            }

            OutputCount = outputChannels;
            InputCount = inputChannels;
        }

        protected override NdArray[] NeedPreviousForward(NdArray[] input)
        {
            NdArray[] resultArray = new NdArray[input.Length];

#if DEBUG
            for (int i = 0; i < input.Length; i++)
#else
            Parallel.For(0, input.Length, i =>
#endif
            {
                int outputSize = (int)Math.Floor((input[i].Shape[2] - this._kSize + this._pad * 2.0) / this._stride) + 1;

                NdArray result = NdArray.Zeros(OutputCount, outputSize, outputSize);

                for (int j = 0; j < OutputCount; j++)
                {
                    for (int y = 0; y < outputSize; y++)
                    {
                        for (int x = 0; x < outputSize; x++)
                        {
                            for (int k = 0; k < InputCount; k++)
                            {
                                for (int dy = 0; dy < this._kSize; dy++)
                                {
                                    for (int dx = 0; dx < this._kSize; dx++)
                                    {
                                        int inputIndexX = x * this._stride + dx - this._pad;
                                        int inputIndexY = y * this._stride + dy - this._pad;

                                        if (inputIndexY >= 0 && inputIndexY < input[i].Shape[1] &&
                                            inputIndexX >= 0 && inputIndexX < input[i].Shape[2])
                                        {
                                            result.Data[result.GetIndex(j, y, x)] +=
                                                input[i].Get(k, inputIndexY, inputIndexX) * this.W.Get(j, k, dy, dx);
                                        }
                                    }
                                }
                            }

                            result.Data[result.GetIndex(j, y, x)] += this.b.Data[j];
                        }
                    }
                }

                resultArray[i] = result;
            }
#if !DEBUG
            );
#endif

            return resultArray;
        }

        protected override NdArray[] NeedPreviousBackward(NdArray[] gy, NdArray[] prevInput, NdArray[] prevOutput)
        {
            NdArray[] resultArray = new NdArray[gy.Length];

#if DEBUG
            for (int i = 0; i < gy.Length; i++)
#else
            Parallel.For(0, gy.Length, i =>
#endif
            {
                NdArray gx = NdArray.ZerosLike(prevInput[i]);

                for (int k = 0; k < gy[i].Shape[0]; k++)
                {
                    for (int j = 0; j < prevInput[i].Shape[0]; j++)
                    {
                        for (int y = 0; y < gy[i].Shape[1]; y++)
                        {
                            for (int x = 0; x < gy[i].Shape[2]; x++)
                            {
                                for (int dy = 0; dy < this.gW.Shape[2]; dy++)
                                {
                                    for (int dx = 0; dx < this.gW.Shape[3]; dx++)
                                    {
                                        int prevIndexY = y * this._stride + dy - this._pad;
                                        int prevIndexX = x * this._stride + dx - this._pad;

                                        if (prevIndexY >= 0 && prevIndexY < prevInput[i].Shape[1] &&
                                            prevIndexX >= 0 && prevIndexX < prevInput[i].Shape[2])
                                        {
                                            this.gW.Data[this.gW.GetIndex(k, j, dy, dx)] +=
                                                prevInput[i].Get(j, prevIndexY, prevIndexX) * gy[i].Get(k, y, x);
                                        }
                                    }
                                }
                            }
                        }
                    }
                }

                for (int j = 0; j < gx.Shape[0]; j++)
                {
                    for (int k = 0; k < gy[i].Shape[0]; k++)
                    {
                        for (int y = 0; y < gy[i].Shape[1]; y++)
                        {
                            for (int x = 0; x < gy[i].Shape[2]; x++)
                            {
                                for (int dy = 0; dy < this._kSize; dy++)
                                {
                                    for (int dx = 0; dx < this._kSize; dx++)
                                    {
                                        int outputIndexY = y * this._stride + dy - this._pad;
                                        int outputIndexX = x * this._stride + dx - this._pad;

                                        if (outputIndexY >= 0 && outputIndexY < gx.Shape[1] &&
                                            outputIndexX >= 0 && outputIndexX < gx.Shape[2]
                                            )
                                        {
                                            gx.Data[gx.GetIndex(j, outputIndexY, outputIndexX)] +=
                                                this.W.Get(k, j, dy, dx) *
                                                gy[i].Data[gy[i].GetIndex(k, y, x)];
                                        }
                                    }
                                }
                            }
                        }
                    }
                }

                if (this.gb != null)
                {
                    for (int j = 0; j < gy[i].Shape[0]; j++)
                    {
                        for (int k = 0; k < gy[i].Shape[1]; k++)
                        {
                            for (int l = 0; l < gy[i].Shape[2]; l++)
                            {
                                this.gb.Data[j] += gy[i].Get(j, k, l);
                            }
                        }
                    }
                }

                resultArray[i] = gx;
            }
#if !DEBUG
            );
#endif

            return resultArray;
        }
    }
}
