using System;
using System.Collections;
using System.Net;
using System.Net.Sockets;
using com.ktgame.datetime;
using Cysharp.Threading.Tasks;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace com.ktgame.unbiased_time
{
	public class NtpDateTime : IDateTime
	{
		private const string NtpServer = "pool.ntp.org";
		private const int RequestTimeout = 3;
		private readonly UnbiasedTime _unbiasedTime;
		private Socket _socket;
		private DateTime _ntpDate;
		private float _responseReceivedTime;
		private bool _synchronized;
		private bool _responseReceived;
		private byte[] _receivedNtpData;

		public DateTime Now
		{
			get
			{
#if UNITY_EDITOR || DEVELOPMENT
                return DateTime.Now;
#else
				if (_synchronized)
				{
					return _ntpDate.AddSeconds(Time.realtimeSinceStartup - _responseReceivedTime);
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
					return _ntpDate.ToUniversalTime().AddSeconds(Time.realtimeSinceStartup - _responseReceivedTime);
				}

				return _unbiasedTime.Now.ToUniversalTime();
#endif
			}
		}

		public UnbiasedTime UnbiasedTime => _unbiasedTime;

		public NtpDateTime()
		{
			_unbiasedTime = new UnbiasedTime();
			_unbiasedTime.SessionStart();
			Synchronize().Forget();
		}

		private async UniTaskVoid Synchronize()
		{
			_synchronized = false;
			_responseReceived = false;
			await SynchronizeDateAsync();
		}

		private async UniTask SynchronizeDateAsync()
		{
			await UniTask.DelaySeconds(RequestTimeout);
			while (true)
			{
				if (!_synchronized)
				{
					if (ConnectionEnabled())
					{
						await UniTask.SwitchToThreadPool();
						await SynchronizeDate();
						await UniTask.SwitchToMainThread();
						await WaitForResponse();
					}

					await UniTask.DelaySeconds(RequestTimeout);
				}
				else
				{
					break;
				}
			}
		}

		private async UniTask SynchronizeDate()
		{
			var ntpData = new byte[48];
			ntpData[0] = 0x1B;

			var addresses = (await Dns.GetHostEntryAsync(NtpServer)).AddressList;
			var ipEndPoint = new IPEndPoint(addresses[0], 123);

			_socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
			_socket.ReceiveTimeout = RequestTimeout * 1000;

			try
			{
				await _socket.ConnectAsync(ipEndPoint);

#if UNITY_2021_3_OR_NEWER
				await _socket.SendAsync(ntpData, SocketFlags.None);
				await _socket.ReceiveAsync(ntpData, SocketFlags.None);
#else
                _socket.Send(ntpData);
                _socket.Receive(ntpData);
#endif
				_responseReceived = true;
				_receivedNtpData = ntpData;
			}
			catch (SocketException se)
			{
				Debug.Log($"[NtpDateTime] NTP sync with socket exception. {se.Message}");
			}
			catch (ArgumentException ae)
			{
				Debug.Log($"[NtpDateTime] NTP sync with argument exception. {ae.Message}");
			}
			catch (Exception e)
			{
				Debug.Log($"[NtpDateTime] NTP sync with exception. {e.Message}");
			}
			finally
			{
				_socket.Close();
				_socket = null;
			}
		}

		private IEnumerator WaitForResponse()
		{
			while (!_responseReceived)
			{
				yield return 0;
			}

			_responseReceivedTime = Time.realtimeSinceStartup;
			var intPart = ((ulong)_receivedNtpData[40] << 24) | ((ulong)_receivedNtpData[41] << 16) | ((ulong)_receivedNtpData[42] << 8) | _receivedNtpData[43];
			var fractPart = ((ulong)_receivedNtpData[44] << 24) | ((ulong)_receivedNtpData[45] << 16) | ((ulong)_receivedNtpData[46] << 8)
							| _receivedNtpData[47];
			var milliseconds = intPart * 1000 + fractPart * 1000 / 0x100000000L;
			_ntpDate = new DateTime(1900, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMilliseconds((long)milliseconds).ToLocalTime();
			_synchronized = true;
			Debug.Log($"[NtpDateTime] Date is synchronized: {_ntpDate}");
		}

		private static bool ConnectionEnabled()
		{
			return Application.internetReachability != NetworkReachability.NotReachable;
		}
	}
}
