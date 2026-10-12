using System;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Assertions.Must;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;
using static UnityEditor.EditorGUILayout;
using AnimatorController = UnityEditor.Animations.AnimatorController;

namespace VRLabs.CustomObjectSyncCreator
{
	public class CustomObjectSyncCreatorWindow : EditorWindow
	{
		private CustomObjectSyncCreator creator;

		[MenuItem("VRLabs/Custom Object Sync")]
		public static void OpenWindow()
		{
			EditorWindow w = GetWindow<CustomObjectSyncCreatorWindow>();
			w.titleContent = new GUIContent("Custom Object Sync");
		}
		
		Vector2 scrollPosition = Vector2.zero;

		private void OnGUI()
		{
			if (creator == null)
			{
				creator = CustomObjectSyncCreator.instance;
			}

			if (creator.resourcePrefab == null)
			{
				creator.resourcePrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/VRLabs/CustomObjectSync/Resources/Custom Object Sync.prefab");
			}
			if (creator.resourcePrefab == null)
			{
				creator.resourcePrefab =  AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath("d51eb264fa89a5b4d9b95f344f169766") ?? "");
			}

			
			if (creator.resourcePrefab == null)
			{
				Debug.LogError("Prefab Asset not found. Please reimport the Custom Object Sync package.");
				return; 
			}
			
			GUILayout.Space(2);
			
			using (new VerticalScope(GUI.skin.box))
			{
				using (var scrollViewScope = new GUILayout.ScrollViewScope(scrollPosition))
				{
					scrollPosition = scrollViewScope.scrollPosition;
					if (creator.useMultipleObjects)
					{
						SerializedObject so = new SerializedObject(creator);
						SerializedProperty prop = so.FindProperty("syncObjects");
						ReorderableList list = new ReorderableList(so, prop, true, true, true, true);
						list.drawHeaderCallback = rect => { EditorGUI.LabelField(rect, "Objects To Sync"); };
						list.drawElementCallback = (rect, index, active, focused) =>
						{
							SerializedProperty element = prop.GetArrayElementAtIndex(index);
							EditorGUI.ObjectField(rect, element, typeof(GameObject));
						};
						list.DoLayoutList();
						so.ApplyModifiedProperties();
					}
					else
					{
						creator.syncObject = (GameObject)ObjectField("Object To Sync", creator.syncObject,
							typeof(GameObject), true);
					}

					GUILayout.Space(2);
					using (new GUILayout.HorizontalScope())
					{
						creator.useMultipleObjects =
							GUILayout.Toggle(creator.useMultipleObjects, new GUIContent("Sync Multiple Objects", "Sync multiple objects at once. This will increase sync times."));
						creator.quickSync = GUILayout.Toggle(creator.quickSync,
							new GUIContent("Quick Sync",
								"This will lower customizability but increase sync times by using 1 float per variable."));
					}


					if (!creator.ObjectPredicate(x => x != null))
					{
						GUILayout.Label("Please select an object to sync.");
						return;
					}

					if (creator.useMultipleObjects && creator.syncObjects.Count(x => x != null) !=
					    creator.syncObjects.Where(x => x != null).Distinct().Count())
					{
						GUILayout.Label("Please select distinct objects to sync.");
						return;
					}

					using (new HorizontalScope(GUI.skin.box))
					{
						creator.installToPrefab = GUILayout.Toggle(creator.installToPrefab, new GUIContent("Install Into Prefab (VRCFury / Modular Avatar)",
							"Installs the system into a prefab instead of the avatar. The avatar's FX controller, parameters and menu are left untouched, " +
							"and a standalone controller, parameters and menu are generated for the prefab's own merge component (e.g. a VRCFury Full Controller)."));
					}

					if (creator.installToPrefab)
					{
						creator.prefabRoot = (GameObject)ObjectField(new GUIContent("Prefab Root",
							"The root of the prefab, e.g. the object with the VRCFury Full Controller. Generated animation paths are relative to this object."),
							creator.prefabRoot, typeof(GameObject), true);

						if (creator.prefabRoot == null)
						{
							GUILayout.Label("Please select the root of the prefab to install into.");
							return;
						}

						if (creator.prefabRoot.GetComponent<VRCAvatarDescriptor>() != null)
						{
							GUILayout.Label("Prefab Root has a VRC Avatar Descriptor. Disable \"Install Into Prefab\" to install directly onto the avatar.");
							return;
						}

						Transform installRoot = creator.prefabRoot.transform;

						if (!creator.ObjectPredicate(x => x.transform != installRoot && x.transform.IsChildOf(installRoot), any: false))
						{
							GUILayout.Label("Object is not a child of the Prefab Root. Please select objects that are children of the Prefab Root.");
							return;
						}

						if (creator.ObjectPredicate(x => PrefabUtility.IsPartOfPrefabInstance(x)))
						{
							GUILayout.Label("Object is part of a prefab instance and can't be moved. Please open the prefab in Prefab Mode, or unpack it, before continuing.");
							return;
						}

						if (installRoot.Find("Custom Object Sync") != null)
						{
							DisplayAlreadyInstalledGUI("prefab");
							return;
						}
					}
					else
					{
						VRCAvatarDescriptor descriptor = creator.syncObjects.FirstOrDefault(x => x != null)
							?.GetComponentInParent<VRCAvatarDescriptor>();

						if (!descriptor)
						{
							GUILayout.Label(
								"Object is not a child of an Avatar. Please select an object that is a child of an Avatar, or enable \"Install Into Prefab\".");
							return;
						}

						if (descriptor.baseAnimationLayers
							    .FirstOrDefault(x => x.type == VRCAvatarDescriptor.AnimLayerType.FX).animatorController !=
						    null &&
						    ((AnimatorController)descriptor.baseAnimationLayers
							    .FirstOrDefault(x => x.type == VRCAvatarDescriptor.AnimLayerType.FX).animatorController)
						    .layers
						    .Any(x => x.name.Contains("CustomObjectSync")))
						{
							DisplayAlreadyInstalledGUI("avatar");
							return;
						}

						if (creator.ObjectPredicate(x => Equals(descriptor.gameObject, x)))
						{
							GUILayout.Label(
								"Object has a VRC Avatar Descriptor. The object you select should be the object you want to sync, not the avatar.");
							return;
						}
						
						GUILayout.Space(2);
						
						creator.menuLocation = TextField(new GUIContent("Menu Location", "The menu path to the menu in which the enable toggle will be placed. e.g. /Props/Ball"), creator.menuLocation);

						VRCExpressionsMenu menu = creator.GetMenuFromLocation(descriptor, creator.menuLocation);

						if (creator.menuLocation != "" && creator.menuLocation != "/")
						{
							if (menu == null || menu.controls == null)
							{
								GUILayout.Label("Menu Location not found. Please select a valid menu location (e.g. /Props/Ball)");
								return;
							}
						}

						if (menu != null && (menu.controls.Count == 8 || (creator.addLocalDebugView && menu.controls.Count >= 7)))
						{
							GUILayout.Label("Menu Location is too full. Please select a menu location that has more space left or make some space in your selected menu location");
							return;
						}
					}

					GUILayout.Space(2);

					using (new HorizontalScope(GUI.skin.box))
					{
						creator.writeDefaults = GUILayout.Toggle(creator.writeDefaults, new GUIContent("Write Defaults", "Whether to use Write Defaults on or Write Defaults off for the generated states."));
						creator.addLocalDebugView = GUILayout.Toggle(creator.addLocalDebugView,
							new GUIContent("Add Local Debug View",
								"Adds a local debug view to the object. This will show the remote position and rotation of the object, locally."));
					}

					GUILayout.Space(2);

					if (creator.quickSync)
					{
						DisplayQuickSyncGUI();
					}
					else
					{
						DisplayBitwiseGUI();
					}

					GUILayout.Space(2);

					if (GUILayout.Button("Generate Custom Sync"))
					{
						EditorApplication.delayCall += creator.Generate;
					}
				}
			}
		}
		
		private void DisplayAlreadyInstalledGUI(string target)
		{
			using (new HorizontalScope(GUI.skin.box))
			{
				GUILayout.FlexibleSpace();
				using (new VerticalScope())
				{
					GUILayout.Label(
						$"Custom Object Sync found on {target}. Multiple custom object syncs not supported.",
						new[] { GUILayout.ExpandWidth(true) });
					using (new HorizontalScope())
					{
						GUILayout.FlexibleSpace();
						GUILayout.Label($"Please remove previous custom object sync before continuing.",
							new[] { GUILayout.ExpandWidth(true) });
						GUILayout.FlexibleSpace();
					}

					if (GUILayout.Button("Remove Custom Sync"))
					{
						creator.Remove();
					}
				}

				GUILayout.FlexibleSpace();
			}
		}
		
		private void DisplayQuickSyncGUI()
		{
			creator.maxRadius = 8 - creator.positionPrecision;
			creator.rotationPrecision = 8;
			
			string positionString = $"Position Precision: {FloatToStringConverter.Convert((float)Math.Pow(0.5, creator.positionPrecision) * 100)}cm";
			creator.positionPrecision = DisplayInt(positionString,  creator.positionPrecision, 1, 8);

			GUILayout.Space(2);
			using (new HorizontalScope(GUI.skin.box))
			{
				GUILayout.Label($"Radius: {Math.Pow(2, 8 - creator.positionPrecision)}m");
			}


			GUILayout.Space(2);
			
			int objectCount = creator.useMultipleObjects ? creator.syncObjects.Count(x => x != null): 1;

			using (new HorizontalScope(GUI.skin.box))
			{
				bool prev = creator.rotationEnabled;
				bool now = GUILayout.Toggle(prev, "Enable Rotation Sync");
				if (now != prev)
				{
					creator.syncRotationX = now;
					creator.syncRotationY = now;
					creator.syncRotationZ = now;
				}
			}

			// Show per-axis checkboxes only when rotation sync is enabled
			if (creator.rotationEnabled)
			{
				using (new HorizontalScope(GUI.skin.box))
				{
					GUILayout.Label("Rotation Axes (Experimental):", GUILayout.ExpandWidth(false));
					creator.syncRotationX = GUILayout.Toggle(creator.syncRotationX, "X");
					creator.syncRotationY = GUILayout.Toggle(creator.syncRotationY, "Y");
					creator.syncRotationZ = GUILayout.Toggle(creator.syncRotationZ, "Z");
				}
			}

			GUILayout.Space(2);

			using (new HorizontalScope(GUI.skin.box))
			{
				creator.addDampeningConstraint = GUILayout.Toggle(creator.addDampeningConstraint, "Add Damping Constraint to Object");
				if (creator.addDampeningConstraint)
				{
					GUILayout.Space(5);
					creator.dampingConstraintValue = EditorGUILayout.Slider("Damping Value", creator.dampingConstraintValue, 0, 1);
				}
			}

			GUILayout.Space(2);

			int objectParameterCount = Mathf.CeilToInt(Mathf.Log(objectCount, 2));
			GUI.contentColor = Color.white;

			using (new HorizontalScope(GUI.skin.box))
			{
				GUILayout.FlexibleSpace();
				int bitUsage = creator.GetMaxBitCount();
				GUILayout.Label($"Parameter Usage: {objectParameterCount + bitUsage + 1}, Time per Sync: {(objectCount * (1/5f)), 4:F3}s", new  [] { GUILayout.ExpandWidth(true) });
				GUILayout.FlexibleSpace();
			}
		}
		private void DisplayBitwiseGUI()
		{
			string positionString = $"Position Precision: {FloatToStringConverter.Convert((float)Math.Pow(0.5, creator.positionPrecision - 1) * 100)}cm";
			creator.positionPrecision = DisplayInt(positionString,  creator.positionPrecision, 2, 16);

			GUILayout.Space(2);

			if (creator.rotationEnabled)
			{
				string rotationString = $"Rotation Precision: {(360.0 / Math.Pow(2, creator.rotationPrecision)).ToString("G3")}\u00b0";
				creator.rotationPrecision = DisplayInt(rotationString, creator.rotationPrecision, 0, 16);

				GUILayout.Space(2);
			}

			creator.maxRadius = DisplayInt($"Radius: {(Math.Pow(2, creator.maxRadius)).ToString("G5")}m", creator.maxRadius, 3, 13);

			if (creator.maxRadius > 11)
			{
				Color oldColor = GUI.color;
				GUI.color = Color.yellow;
				GUILayout.Label("Warning: Radius is very high.\nMost worlds don't need this high radius, and it will cause the position to jitter when walking around.");
				GUI.color = oldColor;
			}
			
			GUILayout.Space(2);

			var maxbitCount = creator.GetMaxBitCount();
			

			int syncSteps = creator.GetStepCount();
			int objectCount = creator.useMultipleObjects ? creator.syncObjects.Count(x => x != null): 1;

			while (syncSteps == creator.GetStepCount(creator.bitCount - 1) && creator.bitCount != 1)
			{
				creator.bitCount--;
			}
			using (new HorizontalScope(GUI.skin.box))
			{
				GUILayout.Label($"Bits per Sync: {creator.bitCount}", new GUILayoutOption[]{ GUILayout.MaxWidth(360)});
				if (GUILayout.Button(EditorGUIUtility.IconContent("d_Toolbar Minus")) && creator.bitCount != 1)
				{
					creator.bitCount--;
					while (creator.GetStepCount() == creator.GetStepCount(creator.bitCount-1) && creator.bitCount != 1)
					{
						creator.bitCount--;
					}
				}

				if (GUILayout.Button(EditorGUIUtility.IconContent("d_Toolbar Plus")) && creator.bitCount != maxbitCount)
				{
					while (syncSteps == creator.GetStepCount(creator.bitCount + 1) && creator.bitCount != maxbitCount)
					{
						creator.bitCount++;
					}

					if (creator.bitCount != maxbitCount + 1)
					{
						creator.bitCount++;
					}
				}
			}
			
			GUILayout.Space(2);

			using (new HorizontalScope(GUI.skin.box))
			{
				bool prev = creator.rotationEnabled;
				bool now = GUILayout.Toggle(prev, "Enable Rotation Sync");
				if (now != prev)
				{
					creator.syncRotationX = now;
					creator.syncRotationY = now;
					creator.syncRotationZ = now;
				}

				creator.centeredOnAvatar = EditorGUILayout.Popup(new GUIContent("Sync Type", 
						"Avatar Centered drops the sync point at the avatar's base (or the Prefab Root when installing into a prefab) when enabling sync. This means radius can be way lower, but it is not late join synced.\n" +
						"World Centered syncs from world origin, which means larger radius is required, but it is late join synced."), (creator.centeredOnAvatar ? 1 : 0), 
					new GUIContent[]
					{
						new GUIContent("World Centered", "Syncs from world origin, which means larger radius is required, but it is late join synced."),
						new GUIContent("Avatar Centered", "Drops the sync point at the avatar's base when enabling sync. This means radius can be way lower, but it is not late join synced.")
					} ) == 1;
			}
			
			// Show per-axis checkboxes only when rotation sync is enabled
			if (creator.rotationEnabled)
			{
				using (new HorizontalScope(GUI.skin.box))
				{
					GUILayout.Label("Rotation Axes:", GUILayout.ExpandWidth(false));
					creator.syncRotationX = GUILayout.Toggle(creator.syncRotationX, "X");
					creator.syncRotationY = GUILayout.Toggle(creator.syncRotationY, "Y");
					creator.syncRotationZ = GUILayout.Toggle(creator.syncRotationZ, "Z");
				}
			}
			
			GUILayout.Space(2);

			using (new HorizontalScope(GUI.skin.box))
			{
				creator.addDampeningConstraint = GUILayout.Toggle(creator.addDampeningConstraint, new GUIContent("Add Damping Constraint to Object", "Adds a damping constraint on the remote side, this makes the object slowly move to the new synced point instead of snapping to it."));
				if (creator.addDampeningConstraint)
				{
					GUILayout.Space(5);
					creator.dampingConstraintValue = EditorGUILayout.Slider("Damping Value", creator.dampingConstraintValue, 0, 1);
				}
			}

			GUILayout.Space(2);

			int parameterCount = Mathf.CeilToInt(Mathf.Log(syncSteps + 1, 2));
			int objectParameterCount = Mathf.CeilToInt(Mathf.Log(objectCount, 2));
			GUI.contentColor = Color.white;

			using (new HorizontalScope(GUI.skin.box))
			{
				GUILayout.FlexibleSpace();
				float conversionTime = (Math.Max(creator.rotationPrecision, creator.maxRadius + creator.positionPrecision)) * 1.5f / 60f;
				float timeBetweenSyncs = objectCount * syncSteps * (1/5f);
				GUILayout.Label($"Sync Steps: {syncSteps}, Parameter Usage: {objectParameterCount + parameterCount + creator.bitCount + 1}, Sync Rate: {(timeBetweenSyncs), 4:F3}s, Sync Delay: {(timeBetweenSyncs + (2 * conversionTime)), 4:F3}s", new  [] { GUILayout.ExpandWidth(true) });
				GUILayout.FlexibleSpace();
			}
		}

		public int DisplayInt(string s, int value, int lowerBound, int upperbound)
		{
			value = Mathf.Clamp(value, lowerBound, upperbound);
			using (new HorizontalScope(GUI.skin.box))
			{
				GUILayout.Label(s, new GUILayoutOption[]{ GUILayout.MaxWidth(360)});
				if (GUILayout.Button(EditorGUIUtility.IconContent("d_Toolbar Minus")) && value != lowerBound)
				{
					value--;
				}

				if (GUILayout.Button(EditorGUIUtility.IconContent("d_Toolbar Plus")) && value != upperbound)
				{
					value++;
				}
				return value;
			}
		}
	}


	class FloatToStringConverter
	{
		public static string Convert(float value)
		{
			// Handle special cases
			if (float.IsInfinity(value) || float.IsNaN(value))
			{
				return value.ToString();
			}

			// If value is a whole number, use standard formatting with no decimal places
			if (value % 1 == 0)
			{
				return value.ToString("F0", System.Globalization.CultureInfo.InvariantCulture);
			}

			StringBuilder sb = new StringBuilder();
			// Consider negative sign
			if (value < 0)
			{
				sb.Append('-');
				value = Math.Abs(value);
			}

			// Append the whole number part
			long wholePart = (long)value;
			sb.Append(wholePart);


			// Work on the fractional part
			float fractionalPart = value - wholePart;
			if (fractionalPart != 0)
			{
				sb.Append(".");
			}
			int significantDigits = 0;
			bool nonZeroDigitEncountered = false;

			// Handle up to three significant digits in the fractional part
			while (significantDigits < 3)
			{
				fractionalPart *= 10; // Shift the decimal point by one position to the right
				int digit = (int)fractionalPart;
				sb.Append(digit); // Append the digit
				if (digit != 0 || nonZeroDigitEncountered)
				{
					nonZeroDigitEncountered = true;
					significantDigits++;
				}
				fractionalPart -= digit; // Remove the digit we just processed
				if (fractionalPart <= 0)
				{
					break; // No more non-zero digits in the fractional part
				}
			}

			return sb.ToString();
		}
	}
}
