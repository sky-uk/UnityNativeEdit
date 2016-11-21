/*
 * Copyright (c) 2015 Kyungmin Bang
 *
 * Permission is hereby granted, free of charge, to any person obtaining
 * a copy of this software and associated documentation files (the
 * "Software"), to deal in the Software without restriction, including
 * without limitation the rights to use, copy, modify, merge, publish,
 * distribute, sublicense, and/or sell copies of the Software, and to
 * permit persons to whom the Software is furnished to do so, subject to
 * the following conditions:
 *
 * The above copyright notice and this permission notice shall be
 * included in all copies or substantial portions of the Software.
 *
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
 * EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
 * MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT.
 * IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY
 * CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT,
 * TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE
 * SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
 */

using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System;
using System.IO;
using AOT;

public abstract class PluginMsgReceiver : MonoBehaviour
{
	private	int		nReceiverId;

	protected void Start()
	{
		nReceiverId = PluginMsgHandler.getInst().RegisterAndGetReceiverId(this);
	}

	protected void OnDestroy()
	{
		PluginMsgHandler.getInst().RemoveReceiver(nReceiverId);
	}

	protected JsonObject SendPluginMsg(JsonObject jsonMsg)
	{
		return PluginMsgHandler.getInst().SendMsgToPlugin(nReceiverId, jsonMsg);
	}

	public abstract void OnPluginMsgDirect(JsonObject jsonMsg);  
}

public class PluginMsgHandler : MonoBehaviour {
	private static PluginMsgHandler inst;
	public 	static PluginMsgHandler getInst() {		return inst; }

	private static bool 	sPluginInitialized = false;
	private int	snCurReceiverIdx = 0;
	private Dictionary<int, PluginMsgReceiver>		m_dictReceiver = new Dictionary<int, PluginMsgReceiver>();
	
	private static StreamWriter fileWriter = null;

	public delegate void ShowKeyboardDelegate(bool bKeyboardShow, int nKeyHeight);
	public ShowKeyboardDelegate OnShowKeyboard = null; 

	private static string MSG_SHOW_KEYBOARD = "ShowKeyboard";
	private static string DEFAULT_NAME = "NativeEditPluginHandler";
	private static bool   ENABLE_WRITE_LOG = false;
	private static GameObject instance;

	void Awake()
	{
		// Don't destroy on load and make sure there is only one instance existing. Without this the
		// events didn't seem to work after a scene reload.
		if (instance != null)
		{
			Destroy(gameObject);
			return;
		}
		instance = gameObject;
		DontDestroyOnLoad(gameObject);

		int tempRandom = (int) UnityEngine.Random.Range(0, 10000.0f);
		this.name = DEFAULT_NAME + tempRandom.ToString();

		if (ENABLE_WRITE_LOG)
		{
			string fileName = Application.persistentDataPath + "/unity_app.log";
			fileWriter = File.CreateText(fileName);
			fileWriter.WriteLine("[LogWriter] Initialized");
			Debug.Log(string.Format("log location {0}", fileName));
		}
		inst = this;
		this.InitializeHandler();
	}

	void OnDestory()
	{
		FileLog("Application closed");
		if (fileWriter != null) fileWriter.Close();
		fileWriter = null;
		this.FinalizeHandler();
	}

	public int RegisterAndGetReceiverId(PluginMsgReceiver receiver)
	{
		int index = snCurReceiverIdx;
		snCurReceiverIdx++;

		m_dictReceiver[index] = receiver;
		return index;
	}

	public void RemoveReceiver(int nReceiverId)
	{
		m_dictReceiver.Remove(nReceiverId);
	}

	public void FileLog(string strLog, string strTag = "NativeEditBoxLog")
	{
		string strOut = string.Format("[!] {0} {1} [{2}]",strTag, strLog, DateTime.Now.ToLongTimeString());

		if (fileWriter != null)
		{
			fileWriter.WriteLine(strOut);
			fileWriter.Flush();
		}
		
		Debug.Log(strOut);
	}
	public void FileLogError(string strLog)
	{
		string strOut = string.Format("[!ERROR!] {0} [{1}]", strLog, DateTime.Now.ToLongTimeString());
		if (fileWriter != null)
		{
			fileWriter.WriteLine(strOut);
			fileWriter.Flush();
		}
		Debug.Log(strOut);
	}
	public PluginMsgReceiver GetReceiver(int nSenderId)
	{
		return m_dictReceiver[nSenderId];
	}
	
	private void OnMsgFromPlugin(string jsonPluginMsg)
	{
		if (jsonPluginMsg == null) return;

		JsonObject jsonMsg = new JsonObject(jsonPluginMsg);

		string msg = jsonMsg.GetString("msg");

		if (msg.Equals(MSG_SHOW_KEYBOARD))
		{
			bool bShow = jsonMsg.GetBool("show");
			int nKeyHeight = (int)( jsonMsg.GetFloat("keyheight") * (float) Screen.height);
			//FileLog(string.Format("keyshow {0} height {1}", bShow, nKeyHeight));
			if (OnShowKeyboard != null) 
			{
				OnShowKeyboard(bShow, nKeyHeight);
			}
		}
		else
		{
			int nSenderId = jsonMsg.GetInt("senderId");

			// In some cases the receiver might be already removed, for example if a button is pressed
			// that will destoy the receiver while the input field is focused an end editing message
			// will be sent from the plugin after the receiver is already destroyed on Unity side.
			if (m_dictReceiver.ContainsKey(nSenderId))
			{
				PluginMsgReceiver receiver = PluginMsgHandler.getInst().GetReceiver(nSenderId);
				receiver.OnPluginMsgDirect(jsonMsg);
			}
		}
	}
	
	#if UNITY_IPHONE  
	[DllImport ("__Internal")]
	private static extern void _iOS_InitPluginMsgHandler(string unityName);
	[DllImport ("__Internal")]
	private static extern string _iOS_SendUnityMsgToPlugin(int nSenderId, string strMsg);
	[DllImport ("__Internal")]
	private static extern void _iOS_ClosePluginMsgHandler();	

	public void InitializeHandler()
	{
		#if UNITY_EDITOR
		return;
		#endif
		if (sPluginInitialized) return;

		_iOS_InitPluginMsgHandler(this.name);
		sPluginInitialized = true;
	}
	
	public void FinalizeHandler()
	{
		#if UNITY_EDITOR
		return;
		#endif
		_iOS_ClosePluginMsgHandler();
	}

	#elif UNITY_ANDROID 

	private static AndroidJavaClass smAndroid;
	public void InitializeHandler()
	{
		bool isEditor = false;
		#if UNITY_EDITOR
		isEditor = true;
		#endif
		if (isEditor || sPluginInitialized) return;

		smAndroid = new AndroidJavaClass("com.bkmin.android.NativeEditPlugin");
		smAndroid.CallStatic("InitPluginMsgHandler", this.name);
		sPluginInitialized = true;
	}
	
	public void FinalizeHandler()
	{
		bool isEditor = false;
		#if UNITY_EDITOR
		isEditor = true;
		#endif
		if (!isEditor)
			smAndroid.CallStatic("ClosePluginMsgHandler");
	}

	#else
	public void InitializeHandler()
	{
	}
	public void FinalizeHandler()
	{
	}

	#endif

	
	public JsonObject SendMsgToPlugin(int nSenderId, JsonObject jsonMsg)
	{
		bool isEditorOrStandalone = false;
		#if UNITY_EDITOR || UNITY_STANDALONE
		isEditorOrStandalone = true;
		#endif

		if (isEditorOrStandalone)
			return new JsonObject();

		jsonMsg["senderId"] = nSenderId;
		string strJson = jsonMsg.Serialize();

		string strRet = "";
		#if UNITY_IPHONE
		strRet = _iOS_SendUnityMsgToPlugin(nSenderId, strJson);
		#elif UNITY_ANDROID 
		strRet = smAndroid.CallStatic<string>("SendUnityMsgToPlugin", nSenderId, strJson);
		#endif

		JsonObject jsonRet = new JsonObject(strRet);
		return jsonRet;
	}
}
