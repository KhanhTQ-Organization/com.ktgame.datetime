using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace com.ktgame.datetime
{
	public class UnbiasedTime
	{
		private const string AndroidPluginClass = "com.vasilij.unbiasedtime.UnbiasedTime";

		private long _timeOffset = 0;

		public long TimeOffset => _timeOffset;

		public DateTime Now => DateTime.Now.AddSeconds(-1.0f * _timeOffset);

		public void UpdateTimeOffset()
		{
#if UNITY_ANDROID
			UpdateTimeOffsetAndroid();
#elif UNITY_IOS
			UpdateTimeOffsetIOS();
#endif
		}

		public bool IsUsingSystemTime()
		{
#if UNITY_ANDROID
			return UsingSystemTimeAndroid();
#elif UNITY_IOS
			return UsingSystemTimeIOS();
#else
			return true;
#endif
		}

		public void SessionStart()
		{
#if UNITY_ANDROID
			StartAndroid();
#elif UNITY_IOS
			StartIOS();
#endif
		}

		public void SessionEnd()
		{
#if UNITY_ANDROID
			EndAndroid();
#elif UNITY_IOS
			EndIOS();
#endif
		}

#if UNITY_IOS
		[DllImport("__Internal")]
		private static extern void _vtcOnSessionStart();

		[DllImport("__Internal")]
		private static extern void _vtcOnSessionEnd();

		[DllImport("__Internal")]
		private static extern int _vtcTimestampOffset();

		[DllImport("__Internal")]
		private static extern int _vtcUsingSystemTime();

		private void UpdateTimeOffsetIOS()
		{
			if (Application.platform != RuntimePlatform.IPhonePlayer) return;
			_timeOffset = _vtcTimestampOffset();
		}

		private void StartIOS()
		{
			if (Application.platform != RuntimePlatform.IPhonePlayer) return;
			_vtcOnSessionStart();
			_timeOffset = _vtcTimestampOffset();
		}

		private void EndIOS()
		{
			if (Application.platform != RuntimePlatform.IPhonePlayer) return;
			_vtcOnSessionEnd();
		}

		private bool UsingSystemTimeIOS()
		{
			if (Application.platform != RuntimePlatform.IPhonePlayer) return true;
			return _vtcUsingSystemTime() != 0;
		}
#endif

#if UNITY_ANDROID

		private void UpdateTimeOffsetAndroid()
		{
			if (Application.platform != RuntimePlatform.Android) return;
			try
			{
				using var unbiasedTimeClass = new AndroidJavaClass(AndroidPluginClass);
				_timeOffset = unbiasedTimeClass.CallStatic<long>("vtcTimestampOffset");
			}
			catch (Exception e)
			{
				Debug.LogError($"[UnbiasedTime] UpdateTimeOffset failed: {e.Message}");
			}
		}

		private void StartAndroid()
		{
			if (Application.platform != RuntimePlatform.Android) return;
			try
			{
				using var activityClass = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
				using var unbiasedTimeClass = new AndroidJavaClass(AndroidPluginClass);
				using var playerActivityContext = activityClass.GetStatic<AndroidJavaObject>("currentActivity");
				if (playerActivityContext != null)
				{
					unbiasedTimeClass.CallStatic("vtcOnSessionStart", playerActivityContext);
					_timeOffset = unbiasedTimeClass.CallStatic<long>("vtcTimestampOffset");
				}
			}
			catch (Exception e)
			{
				Debug.LogError($"[UnbiasedTime] SessionStart failed: {e.Message}");
			}
		}

		private void EndAndroid()
		{
			if (Application.platform != RuntimePlatform.Android) return;
			try
			{
				using var activityClass = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
				using var unbiasedTimeClass = new AndroidJavaClass(AndroidPluginClass);
				using var playerActivityContext = activityClass.GetStatic<AndroidJavaObject>("currentActivity");
				if (playerActivityContext != null)
				{
					unbiasedTimeClass.CallStatic("vtcOnSessionEnd", playerActivityContext);
				}
			}
			catch (Exception e)
			{
				Debug.LogError($"[UnbiasedTime] SessionEnd failed: {e.Message}");
			}
		}

		private bool UsingSystemTimeAndroid()
		{
			if (Application.platform != RuntimePlatform.Android) return true;
			try
			{
				using var unbiasedTimeClass = new AndroidJavaClass(AndroidPluginClass);
				return unbiasedTimeClass.CallStatic<bool>("vtcUsingDeviceTime");
			}
			catch (Exception e)
			{
				Debug.LogError($"[UnbiasedTime] IsUsingSystemTime failed: {e.Message}");
				return true;
			}
		}
#endif
	}
}
