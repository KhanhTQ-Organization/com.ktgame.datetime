using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using com.ktgame.datetime;
using Cysharp.Threading.Tasks;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace com.ktgame.unbiased_time
{
	public class NtpDateTime : IDateTime, IDisposable
	{
		private const string NtpServer = "pool.ntp.org";
		private const int NtpPort = 123;
		private const int RequestTimeout = 3;
		private const int NtpPacketSize = 48;

		private static readonly DateTime NtpEpoch = new DateTime(1900, 1, 1, 0, 0, 0, DateTimeKind.Utc);

		private readonly UnbiasedTime _unbiasedTime;
		private readonly CancellationTokenSource _cts;

		private DateTime _ntpUtc;
		private double _responseReceivedTime;
		private volatile bool _synchronized;
		private bool _syncRunning;
		private bool _disposed;
		
		public event Action Synchronized;
		
		public bool IsSynchronized => _synchronized;

		public DateTime Now
		{
			get
			{
#if UNITY_EDITOR || DEVELOPMENT
				return DateTime.Now;
#else
				if (_synchronized)
				{
					return SynchronizedUtcNow().ToLocalTime();
				}

				return _unbiasedTime.Now;
#endif
			}
		}

		public DateTime NowUtc
		{
			get
			{
#if UNITY_EDITOR || DEVELOPMENT
				return DateTime.UtcNow;
#else
				if (_synchronized)
				{
					return SynchronizedUtcNow();
				}

				return _unbiasedTime.Now.ToUniversalTime();
#endif
			}
		}

		public UnbiasedTime UnbiasedTime => _unbiasedTime;

		public NtpDateTime()
		{
#if UNITY_2022_2_OR_NEWER
			_cts = CancellationTokenSource.CreateLinkedTokenSource(Application.exitCancellationToken);
#else
			_cts = new CancellationTokenSource();
#endif
			_unbiasedTime = new UnbiasedTime();
			_unbiasedTime.SessionStart();
			StartSynchronize();
		}
		
		public void Resynchronize()
		{
			if (_disposed || _syncRunning)
			{
				return;
			}

			StartSynchronize();
		}

		private void StartSynchronize()
		{
			_syncRunning = true;
			SynchronizeLoop(_cts.Token).Forget();
		}
		
		public async UniTask<bool> WaitForSynchronizationAsync(float timeoutSeconds = 10f, CancellationToken cancellationToken = default)
		{
			if (_synchronized || _disposed)
			{
				return _synchronized;
			}

			using (var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _cts.Token))
			using (linked.CancelAfterSlim(TimeSpan.FromSeconds(Mathf.Max(0f, timeoutSeconds)), DelayType.Realtime))
			{
				await UniTask.WaitUntil(() => _synchronized, PlayerLoopTiming.Update, linked.Token).SuppressCancellationThrow();
			}

			return _synchronized;
		}

		public void Dispose()
		{
			if (_disposed)
			{
				return;
			}

			_disposed = true;
			_cts.Cancel();
		}

		private DateTime SynchronizedUtcNow()
		{
			return _ntpUtc.AddSeconds(Time.realtimeSinceStartupAsDouble - _responseReceivedTime);
		}

		private async UniTaskVoid SynchronizeLoop(CancellationToken ct)
		{
			try
			{
				await SynchronizeUntilSuccess(ct);
			}
			finally
			{
				_syncRunning = false;
			}
		}

		private async UniTask SynchronizeUntilSuccess(CancellationToken ct)
		{
#if UNITY_WEBGL && !UNITY_EDITOR
			Debug.LogWarning("[NtpDateTime] NTP is not supported on WebGL. Using device time.");
			await UniTask.CompletedTask;
#else
			var failures = 0;
			while (!ct.IsCancellationRequested)
			{
				if (ConnectionEnabled())
				{
					NtpResult? result = null;
					try
					{
						result = await UniTask.RunOnThreadPool(() => QueryNtp(), true, ct);
					}
					catch (OperationCanceledException)
					{
						return;
					}
					catch (Exception e)
					{
						failures++;
						if (failures == 1 || failures % 10 == 0)
						{
							Debug.LogWarning($"[NtpDateTime] NTP sync failed (attempt {failures}): {e.Message}");
						}
					}

					if (ct.IsCancellationRequested)
					{
						return;
					}

					if (result.HasValue)
					{
						ApplyResult(result.Value);
						return;
					}
				}

				if (await UniTask.Delay(TimeSpan.FromSeconds(RequestTimeout), DelayType.Realtime, PlayerLoopTiming.Update, ct)
						.SuppressCancellationThrow())
				{
					return;
				}
			}
#endif
		}

		private void ApplyResult(NtpResult result)
		{
			// Bù khoảng thời gian từ lúc nhận gói tin tới lúc quay lại main thread.
			var delaySinceReceive = (Stopwatch.GetTimestamp() - result.ReceivedTimestamp) / (double)Stopwatch.Frequency;
			_ntpUtc = result.Utc.AddSeconds(delaySinceReceive);
			_responseReceivedTime = Time.realtimeSinceStartupAsDouble;
			var resync = _synchronized;
			_synchronized = true;
			Debug.Log($"[NtpDateTime] Date is {(resync ? "re-synchronized" : "synchronized")}: {_ntpUtc:o}");

			try
			{
				Synchronized?.Invoke();
			}
			catch (Exception e)
			{
				Debug.LogException(e);
			}
		}

		// Chạy trên thread pool. Dùng API đồng bộ để ReceiveTimeout có hiệu lực.
		private static NtpResult QueryNtp()
		{
			var addresses = Dns.GetHostAddresses(NtpServer);
			if (addresses == null || addresses.Length == 0)
			{
				throw new InvalidOperationException($"DNS returned no address for {NtpServer}");
			}

			var address = Array.Find(addresses, a => a.AddressFamily == AddressFamily.InterNetwork) ?? addresses[0];
			var data = new byte[NtpPacketSize];
			data[0] = 0x1B; // LI = 0, VN = 3, Mode = 3 (client)

			using (var socket = new Socket(address.AddressFamily, SocketType.Dgram, ProtocolType.Udp))
			{
				socket.SendTimeout = RequestTimeout * 1000;
				socket.ReceiveTimeout = RequestTimeout * 1000;
				socket.Connect(new IPEndPoint(address, NtpPort));

				var sentAt = Stopwatch.GetTimestamp();
				socket.Send(data);
				var received = socket.Receive(data);
				var receivedAt = Stopwatch.GetTimestamp();

				if (received < NtpPacketSize)
				{
					throw new InvalidOperationException($"Invalid NTP response length: {received}");
				}

				var transmitUtc = ParseTransmitTime(data);
				var roundTrip = (receivedAt - sentAt) / (double)Stopwatch.Frequency;
				return new NtpResult(transmitUtc.AddSeconds(roundTrip / 2d), receivedAt);
			}
		}

		private static DateTime ParseTransmitTime(byte[] data)
		{
			var leapIndicator = data[0] >> 6;
			var mode = data[0] & 0x7;
			var stratum = data[1];

			if (mode != 4)
			{
				throw new InvalidOperationException($"Invalid NTP mode: {mode}");
			}

			if (leapIndicator == 3)
			{
				throw new InvalidOperationException("NTP server is not synchronized");
			}

			if (stratum == 0 || stratum > 15)
			{
				throw new InvalidOperationException($"Invalid NTP stratum: {stratum} (kiss-of-death or unsynchronized)");
			}

			ulong intPart = ReadUInt32BigEndian(data, 40);
			ulong fractPart = ReadUInt32BigEndian(data, 44);
			if (intPart == 0 && fractPart == 0)
			{
				throw new InvalidOperationException("NTP transmit timestamp is empty");
			}

			// NTP era: bit cao = 0 nghĩa là đã qua mốc 07/02/2036 (era 1).
			if ((intPart & 0x80000000UL) == 0)
			{
				intPart += 0x100000000UL;
			}

			var milliseconds = intPart * 1000UL + fractPart * 1000UL / 0x100000000UL;
			return NtpEpoch.AddMilliseconds(milliseconds);
		}

		private static ulong ReadUInt32BigEndian(byte[] data, int offset)
		{
			return ((ulong)data[offset] << 24) | ((ulong)data[offset + 1] << 16) | ((ulong)data[offset + 2] << 8) | data[offset + 3];
		}

		private static bool ConnectionEnabled()
		{
			return Application.internetReachability != NetworkReachability.NotReachable;
		}

		private readonly struct NtpResult
		{
			public readonly DateTime Utc;
			public readonly long ReceivedTimestamp;

			public NtpResult(DateTime utc, long receivedTimestamp)
			{
				Utc = utc;
				ReceivedTimestamp = receivedTimestamp;
			}
		}
	}
}
