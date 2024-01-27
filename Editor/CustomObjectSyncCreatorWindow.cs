using System;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Assertions.Must;
using VRC.SDK3.Avatars.Components;
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
		
		private void OnGUI()
		{
			if (creator == null)
			{
				creator = CustomObjectSyncCreator.instance;
			}
			
			creator.resourceController ??= AssetDatabase.LoadAssetAtPath<AnimatorController>("Assets/VRLabs/CustomObjectSync/Resources/Custom Object Sync.controller");
			creator.resourceController ??= AssetDatabase.LoadAssetAtPath<AnimatorController>(AssetDatabase.GUIDToAssetPath("6a164efbb993fd047a252ee32da1039b") ?? "");
			
			if (creator.resourceController == null)
			{
				GUILayout.Label("Asset controller not found. Please reimport the Custom Object Sync package.");
				return;
			}
			
			creator.resourcePrefab ??= AssetDatabase.LoadAssetAtPath<GameObject>("Assets/VRLabs/CustomObjectSync/Resources/Custom Object Sync.prefab");
			creator.resourcePrefab ??= AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath("d51eb264fa89a5b4d9b95f344f169766") ?? "");

			
			if (creator.resourcePrefab == null)
			{
				Debug.LogError("Prefab Asset not found. Please reimport the Custom Object Sync package.");
				return; 
			}
			
			GUILayout.Space(2);

			using (new HorizontalScope(GUI.skin.box))
			{
				using (new HorizontalScope(GUILayout.MaxWidth(500)))
				{
					if (creator.useMultipleObjects)
					{
						SerializedObject so = new SerializedObject(creator);
						SerializedProperty prop = so.FindProperty("syncObjects");
						ReorderableList list = new ReorderableList(so, prop, true, true, true, true);
						list.drawHeaderCallback = rect =>
						{
							EditorGUI.LabelField(rect, "Objects To Sync");
						};
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
						creator.syncObject = (GameObject)ObjectField("Object To Sync", creator.syncObject, typeof(GameObject), true);
					}	
				}
				creator.useMultipleObjects = GUILayout.Toggle(creator.useMultipleObjects, "Sync Multiple Objects");
			} 

			if (!creator.ObjectPredicate(x => x != null))
			{
				GUILayout.Label("Please select an object to sync.");
				return;
			}

			if (creator.useMultipleObjects && creator.syncObjects.Count(x => x != null) != creator.syncObjects.Where(x => x != null).Distinct().Count())
			{
				GUILayout.Label("Please select distinct objects to sync.");
				return;
			}
			
			VRCAvatarDescriptor descriptor = null;
			if (creator.useMultipleObjects)
			{
				descriptor = creator.syncObjects.FirstOrDefault(x => x != null)?.GetComponentInParent<VRCAvatarDescriptor>();
			}
			else
			{
				descriptor = creator.syncObject.GetComponentInParent<VRCAvatarDescriptor>();
			}
			
			if (!descriptor)
			{
				GUILayout.Label("Object is not a child of an Avatar. Please select an object that is a child of an Avatar.");
				return;
			}

			if (descriptor != null &&
			    descriptor.baseAnimationLayers.FirstOrDefault(x => x.type == VRCAvatarDescriptor.AnimLayerType.FX).animatorController != null &&
			    ((AnimatorController)descriptor.baseAnimationLayers.FirstOrDefault(x => x.type == VRCAvatarDescriptor.AnimLayerType.FX).animatorController).layers
			    .Any(x => x.name.Contains("CustomObjectSync")))
			{
				using (new HorizontalScope(GUI.skin.box))
				{
					GUILayout.FlexibleSpace();
					using (new VerticalScope())
					{
						GUILayout.Label($"Custom Object Sync found on avatar. Multiple custom object syncs not supported.", new  [] { GUILayout.ExpandWidth(true) });
						using (new HorizontalScope())
						{
							GUILayout.FlexibleSpace();
							GUILayout.Label($"Please remove previous custom object sync before continuing.", new  [] { GUILayout.ExpandWidth(true) });
							GUILayout.FlexibleSpace();
						}
						if (GUILayout.Button("Remove Custom Sync"))
						{
							creator.Remove();
						}
					}
					GUILayout.FlexibleSpace();
				}

				return;
			}

			
			if (creator.ObjectPredicate(x => x.GetComponent<ParentConstraint>()!= null))
			{
				GUILayout.Label("Object has a Parent Constraint component. Please remove this component to continue.");
				return;
			}
			
			if (creator.ObjectPredicate(x => Equals(descriptor.gameObject, x)))
			{
				GUILayout.Label("Object has a VRC Avatar Descriptor. The object you select should be the object you want to sync, not the avatar.");
				return;
			}

			if (descriptor != null && descriptor.expressionsMenu != null && 
			    descriptor.expressionsMenu.controls != null &&
			    descriptor.expressionsMenu.controls.Count == 8)
			{
				Debug.LogError("Avatar Expression Menu Full. Please make some space in your top level Expression Menu to continue.");
				return;
			}

			GUILayout.Space(2);

			string positionString = $"Position Precision: {FloatToStringConverter.Convert((float)Math.Pow(0.5, creator.positionPrecision) * 100)}cm";
			creator.positionPrecision = DisplayInt(positionString,  creator.positionPrecision, 0, 16);

			GUILayout.Space(2);

			if (creator.rotationEnabled)
			{
				string rotationString = $"Rotation Precision: {(360.0 / Math.Pow(2, creator.rotationPrecision)).ToString("G3")}\u00b0";
				creator.rotationPrecision = DisplayInt(rotationString, creator.rotationPrecision, 0, 16);

				GUILayout.Space(2);
			}

			creator.maxRadius = DisplayInt($"Radius: {(Math.Pow(2, creator.maxRadius)).ToString("G5")}m", creator.maxRadius, 1, 13);

			GUILayout.Space(2);

			var maxbitCount = creator.GetMaxBitCount();
			
			int objectCount = creator.useMultipleObjects ? creator.syncObjects.Count(x => x != null): 1;

			int syncSteps = creator.GetStepCount();
			
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
				creator.rotationEnabled = GUILayout.Toggle(creator.rotationEnabled, "Enable Rotation Sync");
				creator.centeredOnAvatar = EditorGUILayout.Popup("Sync Type", (creator.centeredOnAvatar ? 1 : 0), new string[] {"World Centered", "Avatar Centered"} ) == 1;
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

			int parameterCount = Mathf.CeilToInt(Mathf.Log(syncSteps + 1, 2));
			GUI.contentColor = Color.white;

			using (new HorizontalScope(GUI.skin.box))
			{
				GUILayout.FlexibleSpace();
				GUILayout.Label($"Sync Steps: {syncSteps}, Parameter Usage: {parameterCount + creator.bitCount + 1}, Time per Sync: {syncSteps * (1/5f) + (Math.Max(creator.rotationPrecision, creator.maxRadius + creator.positionPrecision)) * 1.5f/60f, 4:F3}s", new  [] { GUILayout.ExpandWidth(true) });
				GUILayout.FlexibleSpace();
			}

			GUILayout.Space(2);
			
			if (GUILayout.Button("Generate Custom Sync"))
			{
				creator.Generate();
			}
		}
		
		public int DisplayInt(string s, int value, int lowerBound, int uppterbound)
		{
			using (new HorizontalScope(GUI.skin.box))
			{
				GUILayout.Label(s, new GUILayoutOption[]{ GUILayout.MaxWidth(360)});
				if (GUILayout.Button(EditorGUIUtility.IconContent("d_Toolbar Minus")) && value != lowerBound)
				{
					value--;
				}

				if (GUILayout.Button(EditorGUIUtility.IconContent("d_Toolbar Plus")) && value != uppterbound)
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
