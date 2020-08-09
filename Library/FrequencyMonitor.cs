﻿namespace Macabresoft.Zvukosti.Library {

    using Macabresoft.Zvukosti.Library.Tuning;
    using NAudio.Dsp;
    using NAudio.Wave;
    using System;

    /// <summary>
    /// Monitors the frequency of a <see cref="IWaveIn" /> device.
    /// </summary>
    public sealed class FrequencyMonitor : NotifyPropertyChanged {
        internal const int BufferSizeByte = 2048;

        private const int BufferSizeFloat = BufferSizeByte / 4;
        private const float HighestFrequency = 1500f; // May need to make this higher if this tuner goes beyond guitars.
        private const float LowestFrequency = 20f;
        private readonly float[] _buffer = new float[BufferSizeFloat];
        private readonly object _lock = new object();

        private BufferedWaveProvider _bufferedWaveProvider;
        private float _frequency;
        private BiQuadFilter _highPassFilter;
        private int _highPeriod;
        private BiQuadFilter _lowPassFilter;
        private int _lowPeriod;
        private ISampleProvider _sampleProvider;
        private ITuning _tuning;
        private IWaveIn _waveIn;

        /// <summary>
        /// Initializes a new instance of the <see cref="FrequencyMonitor" /> class.
        /// </summary>
        public FrequencyMonitor(IWaveIn waveIn, ITuning tuning) {
            if (this._waveIn != null) {
                this._waveIn.DataAvailable -= this.WaveIn_DataAvailable;
            }

            this._waveIn = waveIn ?? throw new ArgumentNullException(nameof(waveIn));
            this._tuning = tuning ?? throw new ArgumentNullException(nameof(tuning));

            this._waveIn.DataAvailable += this.WaveIn_DataAvailable;
            this._bufferedWaveProvider = new BufferedWaveProvider(this._waveIn.WaveFormat) {
                BufferLength = BufferSizeByte,
                DiscardOnBufferOverflow = true
            };

            if (this._waveIn.WaveFormat.Channels == 2) {
                // adjust the volume of the channels based on selected input later
                this._sampleProvider = _bufferedWaveProvider.ToSampleProvider().ToMono(1f, 1f);
            }
            else if (this._waveIn.WaveFormat.Channels == 1) {
                this._sampleProvider = _bufferedWaveProvider.ToSampleProvider();
            }
            else {
                throw new ArgumentOutOfRangeException("Yo, your input device has way too many channels and I didn't account for it.");
            }

            this._lowPassFilter = BiQuadFilter.LowPassFilter(this.SampleRate, LowestFrequency, 1f);
            this._highPassFilter = BiQuadFilter.HighPassFilter(this.SampleRate, HighestFrequency, 1f);
            this._lowPeriod = (int)Math.Floor(this.SampleRate / HighestFrequency);
            this._highPeriod = (int)Math.Ceiling(this.SampleRate / LowestFrequency);
        }

        /// <summary>
        /// Gets or sets the frequency in Hz.
        /// </summary>
        /// <value>The frequency in Hz.</value>
        public float Frequency {
            get {
                return this._frequency;
            }

            set {
                this.Set(ref this._frequency, value);
            }
        }

        /// <summary>
        /// Gets the sample rate this frequency monitor is currently using.
        /// </summary>
        /// <value>The sample rate this frequency monitor is currently using.</value>
        public int SampleRate {
            get {
                return this._waveIn.WaveFormat.SampleRate;
            }
        }

        /// <summary>
        /// Updates this instance.
        /// </summary>
        public void Update() {
            lock (this._lock) {
                this._sampleProvider.Read(this._buffer, 0, BufferSizeFloat);

                if (this._buffer.Length > 0 && this._buffer[BufferSizeFloat - 2] != 0f) {
                    for (var i = 0; i < this._buffer.Length; i++) {
                        this._buffer[i] = this._highPassFilter.Transform(this._lowPassFilter.Transform(this._buffer[i])) * (float)FastFourierTransform.HammingWindow(i, BufferSizeFloat);
                    }

                    var bufferInformation = this.GetBufferInformation();
                    this.Frequency = bufferInformation.Frequency;
                }
            }
        }

        private BufferInformation GetBufferInformation() {
            if (this._buffer.Length < this._highPeriod) {
                throw new InvalidOperationException("The sample rate isn't large enough for the buffer length.");
            }

            var greatestMagnitude = float.NegativeInfinity;
            var chosenPeriod = -1;

            for (var period = this._lowPeriod; period < this._highPeriod; period++) {
                var sum = 0f;
                for (var i = 0; i < this._buffer.Length - period; i++) {
                    sum += this._buffer[i] * this._buffer[i + period];
                }

                var newMagnitude = sum / this._buffer.Length;
                if (newMagnitude > greatestMagnitude) {
                    chosenPeriod = period;
                    greatestMagnitude = newMagnitude;
                }
            }

            var frequency = (float)this.SampleRate / chosenPeriod;
            return frequency < this._tuning.MinimumFrequency || frequency > this._tuning.MaxinimumFrequency ?
                BufferInformation.Unknown :
                new BufferInformation(frequency, greatestMagnitude);
        }

        private void WaveIn_DataAvailable(object sender, WaveInEventArgs e) {
            if (e.BytesRecorded > 0) {
                this._bufferedWaveProvider.AddSamples(e.Buffer, 0, e.BytesRecorded);
            }
        }
    }
}