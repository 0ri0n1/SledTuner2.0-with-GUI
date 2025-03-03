using System;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using MelonLoader;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SleddersTeleporterNs
{
	// Token: 0x02000002 RID: 2
	public class SleddersTeleporter : MelonMod
	{
		// Token: 0x06000001 RID: 1 RVA: 0x00002050 File Offset: 0x00000250
		public override void OnInitializeMelon()
		{
			SceneManager.sceneLoaded += this.OnSceneLoaded;
			this.createTextBoxes = false;
			this.teleportPlayer = false;
			string directoryName = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
			string text = Path.Combine(directoryName, "TeleporterControls.cfg");
			bool flag = File.Exists(text);
			bool flag2 = !flag;
			if (flag2)
			{
				MelonLogger.Msg("Creating cfg file in " + text);
				StreamWriter streamWriter = new StreamWriter(text);
				streamWriter.WriteLine("keyboard=T");
				streamWriter.WriteLine("controller=JoystickButton8");
				streamWriter.Close();
			}
			MelonLogger.Msg("Reading cfg file from " + text);
			StreamReader streamReader = new StreamReader(text);
			string input = streamReader.ReadToEnd();
			foreach (object obj in Regex.Matches(input, "keyboard=(.+)"))
			{
				Match match = (Match)obj;
				bool success = match.Success;
				if (success)
				{
					this.keyboardKey = (KeyCode)Enum.Parse(typeof(KeyCode), match.Groups[1].Value, true);
					MelonLogger.Msg("Keyboard key: " + this.keyboardKey.ToString());
				}
			}
			foreach (object obj2 in Regex.Matches(input, "controller=(.+)"))
			{
				Match match2 = (Match)obj2;
				bool success2 = match2.Success;
				if (success2)
				{
					this.controllerKey = (KeyCode)Enum.Parse(typeof(KeyCode), match2.Groups[1].Value, true);
					MelonLogger.Msg("Controller key: " + this.controllerKey.ToString());
				}
			}
			MelonLogger.Msg("Teleporter initialized!");
		}

		// Token: 0x06000002 RID: 2 RVA: 0x00002280 File Offset: 0x00000480
		private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
		{
			this.createTextBoxes = false;
			this.player = null;
			bool flag = UnityEngine.Object.FindAnyObjectByType<MapController>() != null;
			if (flag)
			{
				MelonLogger.Msg("Found MapController object");
				this.teleporter = new TeleportMapViewController();
				this.teleportMap = new TeleportMapController();
			}
			this.chatController = UnityEngine.Object.FindAnyObjectByType<ChatController2>();
			bool flag2 = this.chatController != null;
			if (flag2)
			{
				MelonLogger.Msg("Got chat controller instance!");
			}
		}

		// Token: 0x06000003 RID: 3 RVA: 0x000022F8 File Offset: 0x000004F8
		public override void OnUpdate()
		{
			base.OnUpdate();
			bool flag = this.chatController != null;
			if (flag)
			{
				this.chatOpen = this.chatController.FFHDACKPPIN;
			}
			bool flag2 = !this.chatOpen;
			if (flag2)
			{
				bool flag3 = Input.GetKeyDown(this.keyboardKey) || Input.GetKeyDown(this.controllerKey);
				if (flag3)
				{
					MapViewController mapViewController = UnityEngine.Object.FindAnyObjectByType<MapViewController>();
					bool flag4 = mapViewController != null;
					if (flag4)
					{
						MelonLogger.Msg("Got mapViewController instance");
						FieldInfo field = typeof(MapViewController).GetField("FHJHDIABGPF", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.GetField);
						Vector2 vector = (Vector2)field.GetValue(mapViewController);
						MelonLogger.Msg("Cursor map position: " + vector.ToString());
						vector = this.teleportMap.mapToWorldPosition(vector);
						MelonLogger.Msg("World position: " + vector.ToString());
						this.teleporter.teleportPlayer(new Vector3(vector.x, 0f, vector.y), Quaternion.Euler(0f, 0f, 0f));
					}
					else
					{
						GameObject gameObject = GameObject.FindGameObjectWithTag("Player");
						bool flag5 = !((gameObject != null) ? gameObject.GetComponent<SnowmobileController>() : null).hasJoinedChallenge;
						if (flag5)
						{
							this.player = gameObject;
							this.createTextBoxes = !this.createTextBoxes;
						}
						else
						{
							this.createTextBoxes = false;
							MelonLogger.Msg("No player object found!");
						}
					}
				}
				bool flag6 = this.createTextBoxes;
				if (flag6)
				{
					bool flag7 = this.player != null;
					if (flag7)
					{
						Vector3 position = this.player.transform.position;
						this.playerXPos = position.x.ToString();
						position = this.player.transform.position;
						this.playerYPos = position.z.ToString();
					}
					else
					{
						this.createTextBoxes = false;
						MelonLogger.Msg("No player object found!");
					}
				}
				bool flag8 = this.teleportPlayer;
				if (flag8)
				{
					this.teleportPlayer = false;
					bool flag9 = this.player != null;
					if (flag9)
					{
						MelonLogger.Msg("Found player object! Attempting teleport!");
						string[] array = new string[5];
						array[0] = "Current pos: (";
						int num = 1;
						Vector3 position = this.player.transform.position;
						array[num] = position.x.ToString();
						array[2] = ",";
						int num2 = 3;
						position = this.player.transform.position;
						array[num2] = position.z.ToString();
						array[4] = ")";
						MelonLogger.Msg(string.Concat(array));
						Vector3 position2 = new Vector3((float)Convert.ToDouble(this.targetXPos), 0f, (float)Convert.ToDouble(this.targetYPos));
						this.teleporter.teleportPlayer(position2, Quaternion.Euler(0f, 0f, 0f));
						string[] array2 = new string[5];
						array2[0] = "New pos: (";
						int num3 = 1;
						position = this.player.transform.position;
						array2[num3] = position.x.ToString();
						array2[2] = ",";
						int num4 = 3;
						position = this.player.transform.position;
						array2[num4] = position.z.ToString();
						array2[4] = ")";
						MelonLogger.Msg(string.Concat(array2));
						this.targetXPos = "";
						this.targetYPos = "";
					}
					else
					{
						MelonLogger.Msg("No player object found!");
					}
				}
			}
			else
			{
				this.createTextBoxes = false;
			}
		}

		// Token: 0x06000004 RID: 4 RVA: 0x0000269C File Offset: 0x0000089C
		public override void OnGUI()
		{
			base.OnGUI();
			bool flag = this.createTextBoxes;
			if (flag)
			{
				GUI.skin.textField.fontSize = 20;
				GUI.skin.box.fontSize = 20;
				GUI.skin.button.fontSize = 20;
				GUI.Box(new Rect(10f, 10f, 100f, 40f), "Player X: ");
				GUI.Box(new Rect(110f, 10f, 100f, 40f), this.playerXPos);
				GUI.Box(new Rect(10f, 50f, 100f, 40f), "Player Y: ");
				GUI.Box(new Rect(110f, 50f, 100f, 40f), this.playerYPos);
				GUI.Box(new Rect(10f, 90f, 100f, 40f), "Target X:");
				this.targetXPos = GUI.TextField(new Rect(110f, 90f, 100f, 40f), this.targetXPos, 8);
				this.targetXPos = Regex.Replace(this.targetXPos, "[^0-9\\.-]", "");
				GUI.Box(new Rect(10f, 130f, 100f, 40f), "Target Y:");
				this.targetYPos = GUI.TextField(new Rect(110f, 130f, 100f, 40f), this.targetYPos, 8);
				this.targetYPos = Regex.Replace(this.targetYPos, "[^0-9\\.-]", "");
				bool flag2 = GUI.Button(new Rect(10f, 170f, 200f, 40f), "Teleport");
				if (flag2)
				{
					MelonLogger.Msg(string.Concat(new string[]
					{
						"(",
						this.targetXPos,
						",",
						this.targetYPos,
						")"
					}));
					this.createTextBoxes = false;
					this.teleportPlayer = true;
				}
			}
		}

		// Token: 0x06000005 RID: 5 RVA: 0x000028D0 File Offset: 0x00000AD0
		public override void OnApplicationQuit()
		{
			base.OnApplicationQuit();
			SceneManager.sceneLoaded -= this.OnSceneLoaded;
			this.teleportPlayer = false;
			this.createTextBoxes = false;
			this.playerXPos = "";
			this.playerYPos = "";
			MelonLogger.Msg("Teleporter terminated");
		}

		// Token: 0x04000001 RID: 1
		private TeleportMapViewController teleporter;

		// Token: 0x04000002 RID: 2
		private TeleportMapController teleportMap;

		// Token: 0x04000003 RID: 3
		private string playerXPos = "";

		// Token: 0x04000004 RID: 4
		private string playerYPos = "";

		// Token: 0x04000005 RID: 5
		private string targetXPos = "";

		// Token: 0x04000006 RID: 6
		private string targetYPos = "";

		// Token: 0x04000007 RID: 7
		private bool createTextBoxes = false;

		// Token: 0x04000008 RID: 8
		private bool teleportPlayer = false;

		// Token: 0x04000009 RID: 9
		private GameObject player = null;

		// Token: 0x0400000A RID: 10
		private ChatController2 chatController = null;

		// Token: 0x0400000B RID: 11
		private bool chatOpen = false;

		// Token: 0x0400000C RID: 12
		private KeyCode keyboardKey = KeyCode.T;

		// Token: 0x0400000D RID: 13
		private KeyCode controllerKey = KeyCode.JoystickButton8;
	}
}
