using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.VersionControl;
using UnityEngine;
using UnityEngine.Animations;
using VRC.Dynamics;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;
using VRC.SDK3.Dynamics.Constraint.Components;
using VRC.SDK3.Dynamics.Contact.Components;
using static VRC.SDKBase.VRC_AvatarParameterDriver;
using static VRLabs.CustomObjectSyncCreator.ControllerGenerationMethods;
using AnimationCurve = UnityEngine.AnimationCurve;
using GameObject = UnityEngine.GameObject;
using Object = UnityEngine.Object;

namespace VRLabs.CustomObjectSyncCreator
{
	public class CustomObjectSyncCreator : ScriptableSingleton<CustomObjectSyncCreator>
	{
		public GameObject resourcePrefab;
		public int bitCount = 16;
		public int maxRadius = 7;
		public int positionPrecision = 6;
		public int rotationPrecision = 8;
		public bool rotationEnabled = true;
		public bool centeredOnAvatar = false;
		public bool addDampeningConstraint = false;
		public bool useMultipleObjects = false;
		public GameObject[] syncObjects;
		public float dampingConstraintValue = 0.15f;
		public bool quickSync;
		public bool writeDefaults = true;
		public bool addLocalDebugView = false;
		public string menuLocation = "";

		public const string STANDARD_NEW_ANIMATION_FOLDER = "Assets/VRLabs/GeneratedAssets/CustomObjectSync/Animations/";
		public const string STANDARD_NEW_ANIMATOR_FOLDER = "Assets/VRLabs/GeneratedAssets/CustomObjectSync/Animators/";
		public const string STANDARD_NEW_PARAMASSET_FOLDER = "Assets/VRLabs/GeneratedAssets/CustomObjectSync/ExpressionParameters/";
		public const string STANDARD_NEW_MENUASSET_FOLDER = "Assets/VRLabs/GeneratedAssets/CustomObjectSync/ExpressionMenu/";
		
		public GameObject syncObject
		{
			get
			{
				if (syncObjects == null)
				{
					syncObjects = new GameObject[1];
				}
				Array.Resize(ref syncObjects, 1);
				return syncObjects[0];
			}
			set
			{
				if (syncObjects == null)
				{
					syncObjects = new GameObject[1];
				}
				Array.Resize(ref syncObjects, 1);
				syncObjects[0] = value;
			}
			
		}
		
		private string[] axis = new[] { "X", "Y", "Z" };

		public void Generate()
		{
			AnimatorController mergedController = null;

			syncObjects = syncObjects.Where(x => x != null).ToArray();
			
			VRCAvatarDescriptor descriptor = syncObjects.Select(x => x.GetComponentInParent<VRCAvatarDescriptor>()).FirstOrDefault();
			
			if (descriptor == null) return;

			int syncSteps = Mathf.CeilToInt(GetMaxBitCount() / (float)bitCount);
			int syncStepParameterCount = Mathf.CeilToInt(Mathf.Log(syncSteps, 2));
			bool[][] syncStepPerms = GeneratePermutations(syncStepParameterCount);
			int objectCount = syncObjects.Count(x => x != null);
			int objectParameterCount = Mathf.CeilToInt(Mathf.Log(objectCount, 2));
			bool[][] objectPerms = GeneratePermutations(objectParameterCount);
			
			#region Resource Setup

			descriptor.customizeAnimationLayers = true;
			RuntimeAnimatorController runtimeController = descriptor.baseAnimationLayers
				.Where(x => x.type == VRCAvatarDescriptor.AnimLayerType.FX).Select(x => x.animatorController)
				.FirstOrDefault();
			AnimatorController mergeController = runtimeController == null ? null : (AnimatorController) runtimeController;

			VRCExpressionParameters parameterObject = descriptor.expressionParameters;
			VRCExpressionsMenu menuObject = GetMenuFromLocation(descriptor, menuLocation);
			
			Directory.CreateDirectory(STANDARD_NEW_ANIMATOR_FOLDER);
			string uniqueControllerPath = AssetDatabase.GenerateUniqueAssetPath(STANDARD_NEW_ANIMATOR_FOLDER + "CustomObjectSync.controller");
			if (mergeController == null)
			{
				mergeController = new AnimatorController();
				AssetDatabase.CreateAsset(mergeController, uniqueControllerPath);
			}
			else
			{
				AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(mergeController), uniqueControllerPath);
			}

			AssetDatabase.SaveAssets();
			AssetDatabase.Refresh();
			mergedController = AssetDatabase.LoadAssetAtPath<AnimatorController>(uniqueControllerPath);

			if (mergedController == null)
			{
				Debug.LogError("Creation of Controller object failed. Please report this to jellejurre on the VRLabs discord at discord.vrlabs.dev");
				return;
			}
			
			if (parameterObject == null)
			{
				Directory.CreateDirectory(STANDARD_NEW_PARAMASSET_FOLDER);
				string uniquePath = AssetDatabase.GenerateUniqueAssetPath(STANDARD_NEW_PARAMASSET_FOLDER + "Parameters.asset");
				parameterObject = CreateInstance<VRCExpressionParameters>();
				parameterObject.parameters = Array.Empty<VRCExpressionParameters.Parameter>();
				AssetDatabase.CreateAsset(parameterObject, uniquePath);
				AssetDatabase.SaveAssets();
				AssetDatabase.Refresh();
				parameterObject = AssetDatabase.LoadAssetAtPath<VRCExpressionParameters>(uniquePath);
			}

			if (parameterObject == null)
			{
				Debug.LogError("Creation of Parameter object failed. Please report this to jellejurre on the VRLabs discord at discord.vrlabs.dev");
				return;
			}
			
			if (menuObject == null)
			{
				Directory.CreateDirectory(STANDARD_NEW_MENUASSET_FOLDER);
				string uniquePath = AssetDatabase.GenerateUniqueAssetPath(STANDARD_NEW_MENUASSET_FOLDER + "Menu.asset");
				menuObject = CreateInstance<VRCExpressionsMenu>();
				menuObject.controls = new List<VRCExpressionsMenu.Control>();
				AssetDatabase.CreateAsset(menuObject, uniquePath);
				AssetDatabase.SaveAssets();
				AssetDatabase.Refresh();
				menuObject = AssetDatabase.LoadAssetAtPath<VRCExpressionsMenu>(uniquePath);
				descriptor.expressionsMenu = menuObject;
			}
			
			if (menuObject == null)
			{
				Debug.LogError("Creation of Menu object failed. Please report this to jellejurre on the VRLabs discord at discord.vrlabs.dev");
				return;
			}
			
			AnimationClip buffer = GenerateClip("Buffer");
			AddCurve(buffer, "THIS PATH DOES NOT EXIST", typeof(Object), "NOPE", AnimationCurve.Constant(0, 1/60f, 0));

			#endregion
			
			int positionBits = maxRadius + positionPrecision;
			int rotationBits = rotationEnabled ? rotationPrecision : 0;
			
			#region Bit Parameters

			List<AnimatorControllerParameter> parameters = new List<AnimatorControllerParameter>()
			{
				GenerateBoolParameter("IsLocal"),
				GenerateFloatParameter("CustomObjectSync/One", defaultFloat: 1f),
				GenerateFloatParameter("CustomObjectSync/AngleMagX_Angle"),
				GenerateFloatParameter("CustomObjectSync/AngleMagY_Angle"),
				GenerateFloatParameter("CustomObjectSync/AngleMagZ_Angle"),
				GenerateFloatParameter("CustomObjectSync/AngleSignX_Angle"),
				GenerateFloatParameter("CustomObjectSync/AngleSignY_Angle"),
				GenerateFloatParameter("CustomObjectSync/AngleSignZ_Angle"),
				GenerateFloatParameter("CustomObjectSync/RotationX"),
				GenerateFloatParameter("CustomObjectSync/RotationY"),
				GenerateFloatParameter("CustomObjectSync/RotationZ"),
				GenerateFloatParameter("CustomObjectSync/PositionX"),
				GenerateFloatParameter("CustomObjectSync/PositionY"),
				GenerateFloatParameter("CustomObjectSync/PositionZ"),
				GenerateFloatParameter("CustomObjectSync/PositionSignX"),
				GenerateFloatParameter("CustomObjectSync/PositionSignY"),
				GenerateFloatParameter("CustomObjectSync/PositionSignZ"),
				GenerateFloatParameter("CustomObjectSync/PositionXPos"),
				GenerateFloatParameter("CustomObjectSync/PositionXNeg"),
				GenerateFloatParameter("CustomObjectSync/PositionYPos"),
				GenerateFloatParameter("CustomObjectSync/PositionYNeg"),
				GenerateFloatParameter("CustomObjectSync/PositionZPos"),
				GenerateFloatParameter("CustomObjectSync/PositionZNeg"),
				GenerateBoolParameter("CustomObjectSync/SetStage"),
				GenerateBoolParameter("CustomObjectSync/Enabled")
			};


			if (!quickSync)
			{
				AddBitConversionParameters(positionBits, parameters, objectCount, rotationBits);
			}

			for (int b = 0; b < objectParameterCount; b++)
			{
				parameters.Add(GenerateFloatParameter($"CustomObjectSync/LocalReadBit{b}"));
			}

			if (addLocalDebugView)
			{
				parameters.Add(GenerateBoolParameter("CustomObjectSync/LocalDebugView", false));
				parameters = parameters.Concat(axis.Select(x => GenerateFloatParameter($"CustomObjectSync/LocalDebugView/Position{x}", 0f))).ToList();
				parameters = parameters.Concat(axis.Select(x => GenerateIntParameter($"CustomObjectSync/LocalDebugView/PositionTmp{x}", 0))).ToList();
				if (quickSync)
				{
					parameters = parameters.Concat(axis.Select(x => GenerateFloatParameter($"CustomObjectSync/LocalDebugView/PositionSign{x}", 0f))).ToList();
					parameters = parameters.Concat(axis.Select(x => GenerateFloatParameter($"CustomObjectSync/LocalDebugView/PositionTmp2{x}", 0))).ToList();
				}
				if (rotationEnabled)
				{
					parameters = parameters.Concat(axis.Select(x => GenerateFloatParameter($"CustomObjectSync/LocalDebugView/Rotation{x}", 0f))).ToList();
					parameters = parameters.Concat(axis.Select(x => GenerateIntParameter($"CustomObjectSync/LocalDebugView/RotationTmp{x}", 0))).ToList();	
				}
			}
			
			mergedController.parameters = mergedController.parameters.Concat(parameters.Where(p => mergedController.parameters.All(x => x.name != p.name))).ToArray();

			SetupSyncLayerParameters(syncSteps, objectCount, objectParameterCount, syncStepParameterCount, syncStepPerms, mergedController);
			ControllerGenerationMethods.defaultWriteDefaults = writeDefaults;
			
			#endregion

			#region VRCParameters
			List<VRCExpressionParameters.Parameter> parameterList = new List<VRCExpressionParameters.Parameter>();
			parameterList.Add(GenerateVRCParameter("CustomObjectSync/Enabled", VRCExpressionParameters.ValueType.Bool));
			if (quickSync)
			{
				parameterList = parameterList.Concat(axis.Select(x => GenerateVRCParameter($"CustomObjectSync/Sync/Position{x}", VRCExpressionParameters.ValueType.Float))).ToList();
				parameterList = parameterList.Concat(axis.Select(x => GenerateVRCParameter($"CustomObjectSync/Sync/PositionSign{x}", VRCExpressionParameters.ValueType.Bool))).ToList();
				if (rotationEnabled)
				{
					parameterList = parameterList.Concat(axis.Select(x => GenerateVRCParameter($"CustomObjectSync/Sync/Rotation{x}", VRCExpressionParameters.ValueType.Float))).ToList();
				}
			}
			else
			{
				for (int b = 0; b < bitCount; b++)
				{
					parameterList.Add(GenerateVRCParameter($"CustomObjectSync/Sync/Data{b}", VRCExpressionParameters.ValueType.Bool));
				}

				for (int b = 0; b < syncStepParameterCount; b++)
				{
					parameterList.Add(GenerateVRCParameter($"CustomObjectSync/Sync/Step{b}", VRCExpressionParameters.ValueType.Bool, defaultValue:  syncStepPerms[syncSteps-1][b] ? 1 : 0));
				}
			}

			
			for (int b = 0; b < objectParameterCount; b++)
			{
				parameterList.Add(GenerateVRCParameter($"CustomObjectSync/Sync/Object{b}", VRCExpressionParameters.ValueType.Bool));
			}
			
			if (addLocalDebugView)
			{
				parameterList.Add(GenerateVRCParameter("CustomObjectSync/LocalDebugView", VRCExpressionParameters.ValueType.Bool, networkSynced: false));
			}
			

			parameterObject.parameters = parameterObject.parameters.Concat(parameterList).ToArray();
			
			menuObject.controls.Add(new VRCExpressionsMenu.Control()
			{
				name = "Enable Custom Object Sync",
				style = VRCExpressionsMenu.Control.Style.Style1,
				type = VRCExpressionsMenu.Control.ControlType.Toggle,
				parameter = new VRCExpressionsMenu.Control.Parameter()
				{
					name = "CustomObjectSync/Enabled"
				}
			});

			if (addLocalDebugView)
			{
				menuObject.controls.Add(new VRCExpressionsMenu.Control()
				{
					name = "Show Remote Position",
					style = VRCExpressionsMenu.Control.Style.Style1,
					type = VRCExpressionsMenu.Control.ControlType.Toggle,
					parameter = new VRCExpressionsMenu.Control.Parameter()
					{
						name = "CustomObjectSync/LocalDebugView"
					}
				});
			}

			EditorUtility.SetDirty(menuObject);
			EditorUtility.SetDirty(parameterObject);
			#endregion

			GameObject syncSystem = InstallSystem(descriptor, mergedController, parameterObject);
			
			List<AnimatorControllerLayer> layersToAdd = new List<AnimatorControllerLayer>();
			
			if (!quickSync) layersToAdd = GenerateBitConversionLayers(objectCount, buffer, positionBits, objectParameterCount, rotationBits);
			
			AnimatorControllerLayer syncLayer = SetupSyncLayer(syncSteps, positionBits, rotationBits, objectCount, objectParameterCount, objectPerms, syncStepParameterCount, syncStepPerms);
			layersToAdd.Add(syncLayer);
			
			AnimatorControllerLayer displayLayer = SetupDisplayLayer(descriptor, objectCount, syncSystem, buffer, objectParameterCount, objectPerms);
			layersToAdd.Add(displayLayer);

			if (addLocalDebugView)
			{
				layersToAdd.Add(SetupLocalDebugViewLayer(descriptor, objectCount, syncSystem, buffer, objectParameterCount, objectPerms));
			}
			
			mergedController.layers = mergedController.layers.Concat(layersToAdd).ToArray();

			foreach (AnimatorState state in mergedController.layers.Where(x => x.name.Contains("CustomObjectSync")).SelectMany(x => x.stateMachine.states).Select(x => x.state))
			{
				if (state.motion is null)
				{
					state.motion = buffer;
				}
			}
			Directory.CreateDirectory(STANDARD_NEW_ANIMATION_FOLDER);

			try
			{
				AssetDatabase.StartAssetEditing();
				SerializeController(mergedController);
				foreach (var clip in mergedController.animationClips)
				{
					if (String.IsNullOrEmpty(AssetDatabase.GetAssetPath(clip))){
						if (String.IsNullOrEmpty(clip.name))
						{
							clip.name = "Anim";
						}
						var uniqueFileName = AssetDatabase.GenerateUniqueAssetPath($"{STANDARD_NEW_ANIMATION_FOLDER}{clip.name}.anim");
						AssetDatabase.CreateAsset(clip, uniqueFileName);
					}
				}
			}
			finally
			{
				AssetDatabase.StopAssetEditing();
			}


			EditorUtility.DisplayDialog("Success!", "Custom Object Sync has been successfully installed", "Ok");
		}

		private AnimatorControllerLayer SetupDisplayLayer(VRCAvatarDescriptor descriptor, int objectCount,
			GameObject syncSystem, AnimationClip buffer, int objectParameterCount, bool[][] objectPerms)
		{
			string[] targetStrings = syncObjects.Select(x => AnimationUtility.CalculateTransformPath(addDampeningConstraint ? x.transform.parent.Find($"{x.name} Damping Sync") : x.transform, descriptor.transform)).ToArray();
			string[] dampingConstraints = syncObjects.Select(x => AnimationUtility.CalculateTransformPath(x.transform, descriptor.transform)).ToArray();
			float contactBugOffset = Mathf.Pow(2, maxRadius - 6); // Fixes a bug where proximity contacts near edges give 0, so we set this theoretical 0 to far away from spawn to reduce chances of this happening

			AnimationClip enableMeasure = GenerateClip("localMeasureEnabled");
			AddCurve(enableMeasure, "Custom Object Sync/Measure", typeof(GameObject), "m_IsActive", AnimationCurve.Constant(0, 1/60f, 1));
			AddContactCurves(enableMeasure, AnimationCurve.Constant(0, 1f, contactBugOffset / Mathf.Pow(2, maxRadius) * 3));

			AnimationClip remoteVRCParentConstraintOff = GenerateClip("remoteVRCParentConstraintDisabled");
			AddCurve(remoteVRCParentConstraintOff, "Custom Object Sync/Measure", typeof(GameObject), "m_IsActive", AnimationCurve.Constant(0, 1/60f, 0));
			AddCurve(remoteVRCParentConstraintOff, "Custom Object Sync/Set", typeof(VRCPositionConstraint), "m_Enabled", AnimationCurve.Constant(0, 1/60f, 1));

			for (var i = 0; i < targetStrings.Length; i++)
			{
				var targetString = targetStrings[i];
				AddCurve(remoteVRCParentConstraintOff, targetString, typeof(VRCParentConstraint), "Sources.source0.Weight", AnimationCurve.Constant(0, 1 / 60f, 1));
				AddCurve(remoteVRCParentConstraintOff, targetString, typeof(VRCParentConstraint), "Sources.source1.Weight", AnimationCurve.Constant(0, 1 / 60f, 0));
				if (addDampeningConstraint)
				{
					AddCurve(remoteVRCParentConstraintOff, dampingConstraints[i], typeof(VRCParentConstraint), "Sources.source0.Weight", AnimationCurve.Constant(0, 1 / 60f, 1));
					AddCurve(remoteVRCParentConstraintOff, dampingConstraints[i], typeof(VRCParentConstraint), "Sources.source1.Weight", AnimationCurve.Constant(0, 1 / 60f, 0));
				}
				AddCurve(remoteVRCParentConstraintOff, targetString, typeof(VRCParentConstraint), "m_Enabled", AnimationCurve.Constant(0, 1 / 60f, 1));
			}

			AnimationClip disableDamping = GenerateClip("localDisableDamping");
			if (addDampeningConstraint)
			{
				foreach (string targetPath in syncObjects.Select(x => AnimationUtility.CalculateTransformPath(x.transform, descriptor.transform)))
				{
					AddCurve(disableDamping, targetPath, typeof(VRCParentConstraint), "Sources.source0.Weight", AnimationCurve.Constant(0, 1/60f, 1));
					AddCurve(disableDamping, targetPath, typeof(VRCParentConstraint), "Sources.source1.Weight", AnimationCurve.Constant(0, 1/60f, 0));
				}
			}
			
			ChildAnimatorState StateIdleRemote = GenerateChildState(new Vector3(30f, 180f, 0f), GenerateState("Idle/Remote"));
			bool[][] axisPermutations = GeneratePermutations(3);
			List<ChildAnimatorState> displayStates = new List<ChildAnimatorState>();
			AnimationClip[] localConstraintTargetClips = Enumerable.Range(0, objectCount).Select(x =>
			{
				string targetString = AnimationUtility.CalculateTransformPath(syncSystem.transform, descriptor.transform) + "/Target";
				AnimationClip localConstraintOn = GenerateClip($"localVRCParentConstraintEnabled{x}");
				Enumerable.Range(0, objectCount).ToList().ForEach(y=> AddCurve(localConstraintOn, targetString, typeof(VRCParentConstraint), $"Sources.source{y}.Weight", AnimationCurve.Constant(0, 1 / 60f, x == y ? 1 : 0)));
				return localConstraintOn;
			}).ToArray();
			BlendTree localEnableTree = GenerateBlendTree("LocalSetTree", BlendTreeType.Direct);

			Motion RecurseCreateTree(int depth, int index, int max)
			{
				if (depth == 0)
				{
					return index >= localConstraintTargetClips.Length ? buffer : localConstraintTargetClips[index];
				}
					
				BlendTree childTree = GenerateBlendTree($"TargetTree{depth}-{index}", BlendTreeType.Simple1D, blendParameter: $"CustomObjectSync/LocalReadBit{depth - 1}");
				childTree.children = new[]
				{
					GenerateChildMotion(RecurseCreateTree(depth - 1, index * 2, max)),
					GenerateChildMotion(RecurseCreateTree(depth - 1, index * 2 + 1, max))
				};
				return childTree;
			}

			Motion initialTargetTree = RecurseCreateTree(objectParameterCount, 0,(int)Math.Pow(2, objectParameterCount));
			localEnableTree.children = new[]
			{
				GenerateChildMotion(enableMeasure, directBlendParameter: "CustomObjectSync/One"),
				GenerateChildMotion(disableDamping, directBlendParameter: "CustomObjectSync/One"),
				GenerateChildMotion(initialTargetTree, directBlendParameter: "CustomObjectSync/One")
			};

			for (var p = 0; p < axisPermutations.Length; p++)
			{
				bool[] perm = axisPermutations[p];
				AnimatorState stateRot = GenerateState($"X{(perm[0] ? "+" : "-")}Y{(perm[1] ? "+" : "-")}Z{(perm[2] ? "+" : "-")}Rot", motion: localEnableTree, writeDefaultValues: true);
				stateRot.behaviours = new StateMachineBehaviour[] {
					GenerateParameterDriver(
						Enumerable.Range(0, axis.Length).Select(x => GenerateParameter(ChangeType.Copy, source: $"CustomObjectSync/AngleMag{axis[x]}_Angle", name: $"CustomObjectSync/Rotation{axis[x]}",
							convertRange: true, destMax: perm[x] ? 1f : 0f, destMin: 0.5f, sourceMax: 1f) 
						).Append(GenerateParameter(ChangeType.Set, name: "CustomObjectSync/SetStage", value: 1f)).ToArray())
				};
				displayStates.Add(GenerateChildState(new Vector3(-220f, -210f + 60 * p, 0f), stateRot));

				AnimatorState statePos =  GenerateState($"X{(perm[0] ? "+" : "-")}Y{(perm[1] ? "+" : "-")}Z{(perm[2] ? "+" : "-")}Pos", motion: localEnableTree, writeDefaultValues: true);
				if (quickSync)
				{
					statePos.behaviours = new StateMachineBehaviour[] {
						GenerateParameterDriver(
							Enumerable.Range(0, axis.Length).Select(x => GenerateParameter(ChangeType.Copy, source: $"CustomObjectSync/Position{axis[x]}{(perm[x] ? "Pos" : "Neg")}", name: $"CustomObjectSync/Position{axis[x]}"))
								.Concat(Enumerable.Range(0, axis.Length).Select(x => GenerateParameter(ChangeType.Set, name: $"CustomObjectSync/PositionSign{axis[x]}", value: perm[x] ? 1.0f : 0.0f))).Append(GenerateParameter(ChangeType.Set, name: "CustomObjectSync/SetStage", value: 0f)).ToArray())
					};	
				}
				else
				{
					statePos.behaviours = new StateMachineBehaviour[] {
						GenerateParameterDriver(
							Enumerable.Range(0, axis.Length).Select(x => GenerateParameter(ChangeType.Copy, source: $"CustomObjectSync/Position{axis[x]}{(perm[x] ? "Pos" : "Neg")}", name: $"CustomObjectSync/Position{axis[x]}",
								convertRange: true, destMax: perm[x] ? 1f : 0f, destMin: 0.5f, sourceMax: 1f) 
							).Append(GenerateParameter(ChangeType.Set, name: "CustomObjectSync/SetStage", value: 0f)).ToArray())
					};
				}
				displayStates.Add(GenerateChildState(new Vector3(-470f, -210f + 60 * p, 0f), statePos));

			}
			void AddContactCurves(AnimationClip clip, AnimationCurve curve)
			{
				AddCurve(clip, "Custom Object Sync/Measure/Position/SenderX", typeof(VRCPositionConstraint), "PositionOffset.x", curve);
				AddCurve(clip, "Custom Object Sync/Measure/Position/SenderY", typeof(VRCPositionConstraint), "PositionOffset.y", curve);
				AddCurve(clip, "Custom Object Sync/Measure/Position/SenderZ", typeof(VRCPositionConstraint), "PositionOffset.z",  curve);
			}
			
			AnimationClip ContactTimeoutClip = GenerateClip("ContactTimeout");
			AddContactCurves(ContactTimeoutClip, GenerateCurve(new[] { GenerateKeyFrame(0, contactBugOffset / Mathf.Pow(2, maxRadius) * 3), GenerateKeyFrame(0.1f, 10), GenerateKeyFrame(0.2f, contactBugOffset / Mathf.Pow(2, maxRadius) * 3), GenerateKeyFrame(0.3f, contactBugOffset / Mathf.Pow(2, maxRadius) * 3) }));
			

			BlendTree ContactTimeoutTree = GenerateBlendTree("ContactTimeout", BlendTreeType.Direct);
			ContactTimeoutTree.children = new ChildMotion[]
			{
				GenerateChildMotion(motion: localEnableTree, directBlendParameter: "CustomObjectSync/One"),
				GenerateChildMotion(motion: ContactTimeoutClip, directBlendParameter: "CustomObjectSync/One")
			};
			ChildAnimatorState ContactTimeoutState = GenerateChildState(new Vector3(-470f, -270f, 0f), GenerateState("ContactTimeout", motion: ContactTimeoutTree, writeDefaultValues: true));
			List<ChildAnimatorState> remoteOnStates = new List<ChildAnimatorState>();
			AnimationClip[] constraintsEnabled = Enumerable.Range(0, targetStrings.Length).Select(i =>
			{
				string targetString = targetStrings[i];
				AnimationClip remoteVRCParentConstraintOn = GenerateClip($"remoteVRCParentConstraintEnabled{i}");
				AddCurve(remoteVRCParentConstraintOn, "Custom Object Sync/Measure", typeof(GameObject), "m_IsActive", AnimationCurve.Constant(0, 1/60f, 0));
				AddCurve(remoteVRCParentConstraintOn, "Custom Object Sync/Set", typeof(VRCPositionConstraint), "m_Enabled", AnimationCurve.Constant(0, 1/60f, 0));
				AddCurve(remoteVRCParentConstraintOn, targetString, typeof(VRCParentConstraint), "m_Enabled", AnimationCurve.Linear(0, 0, 2/60f, 1));
				AddCurve(remoteVRCParentConstraintOn, targetString, typeof(VRCParentConstraint), "Sources.source0.Weight", AnimationCurve.Constant(0, 1/60f, 0));
				AddCurve(remoteVRCParentConstraintOn, targetString, typeof(VRCParentConstraint), "Sources.source1.Weight", AnimationCurve.Constant(0, 1/60f, 1));
				if (addDampeningConstraint)
				{
					AddCurve(remoteVRCParentConstraintOn, dampingConstraints[i], typeof(VRCParentConstraint), "Sources.source0.Weight", AnimationCurve.Constant(0, 1/60f, dampingConstraintValue));
					AddCurve(remoteVRCParentConstraintOn, dampingConstraints[i], typeof(VRCParentConstraint), "Sources.source1.Weight", AnimationCurve.Constant(0, 1/60f, 1));
				}
				return remoteVRCParentConstraintOn;
			}).ToArray();
			AnimationClip[] constraintsDisabled = Enumerable.Range(0, targetStrings.Length).Select(i =>
			{
				string targetString = targetStrings[i];
				AnimationClip remoteVRCParentConstraintOffAnim = GenerateClip($"remoteVRCParentConstraintDisabled{i}");
				AddCurve(remoteVRCParentConstraintOffAnim, targetString, typeof(VRCParentConstraint), "m_Enabled", AnimationCurve.Constant(0, 1/60f, 0));
				AddCurve(remoteVRCParentConstraintOffAnim, targetString, typeof(VRCParentConstraint), "Sources.source0.Weight", AnimationCurve.Constant(0, 1/60f, 0));
				AddCurve(remoteVRCParentConstraintOffAnim, targetString, typeof(VRCParentConstraint), "Sources.source1.Weight", AnimationCurve.Constant(0, 1/60f, 1));
				if (addDampeningConstraint)
				{
					AddCurve(remoteVRCParentConstraintOffAnim, dampingConstraints[i], typeof(VRCParentConstraint), "Sources.source0.Weight", AnimationCurve.Constant(0, 1/60f, 1));
					AddCurve(remoteVRCParentConstraintOffAnim, dampingConstraints[i], typeof(VRCParentConstraint), "Sources.source1.Weight", AnimationCurve.Constant(0, 1/60f, 0));
				}
				return remoteVRCParentConstraintOffAnim;
			}).ToArray();
			
			for (int o = 0; o < objectCount; o++)
			{
				AnimatorState remoteOnState = GenerateState("Remote On", writeDefaultValues: true);
				var remoteTree = GenerateSetterTree(false);
				remoteTree.children = remoteTree.children.Concat(Enumerable.Range(0, objectCount).Select(o2 => GenerateChildMotion(o2 == o ? constraintsEnabled[o2] : constraintsDisabled[o2], directBlendParameter: "CustomObjectSync/One"))).ToArray();
				
				remoteOnState.motion = remoteTree;	
				displayStates.Add(GenerateChildState(new Vector3(260f, -60f * (o + 1), 0f), remoteOnState));
				remoteOnStates.Add(displayStates.Last());
			}

			AnimatorState StateRemoteOff = GenerateState("Remote Off", motion: remoteVRCParentConstraintOff);
			displayStates.Add(GenerateChildState(new Vector3(260f, 0, 0f), StateRemoteOff));
			
			AnimatorStateMachine displayStateMachine = GenerateStateMachine("CustomObjectSync/Parameter Setup and Display", new Vector3(50f, 20f, 0f), new Vector3(50f, 120f, 0f), new Vector3(800f, 120f, 0f), states: displayStates.Append(ContactTimeoutState).ToArray(), defaultState: StateIdleRemote.state);

			List<AnimatorStateTransition> anyStateTransitions = new List<AnimatorStateTransition>();
			anyStateTransitions.Add(GenerateTransition("", conditions:
				new AnimatorCondition[]
				{
					GenerateCondition(AnimatorConditionMode.IfNot, "IsLocal", 0f),
					GenerateCondition(AnimatorConditionMode.IfNot, "CustomObjectSync/Enabled", 0f)
				}, destinationState: StateRemoteOff));
			
			string objParam = quickSync ? "CustomObjectSync/Sync/Object" : "CustomObjectSync/Object";
			for (var o = 0; o < remoteOnStates.Count; o++)
			{
				int index = quickSync ? (o + 1) % objectCount : o;
				anyStateTransitions.Add(GenerateTransition("", conditions:
					Enumerable.Range(0, objectParameterCount).Select(x => GenerateCondition(objectPerms[index][x] ? AnimatorConditionMode.If : AnimatorConditionMode.IfNot, $"{objParam}{x}", threshold: 0))
						.Append(GenerateCondition(AnimatorConditionMode.IfNot, "IsLocal", 0f))
						.Append(GenerateCondition(AnimatorConditionMode.If, "CustomObjectSync/Enabled", 0f)).ToArray()
					, destinationState: remoteOnStates[o].state));
			}

			
			for (int p = 0; p < axisPermutations.Length; p++)
			{
				bool[] perm = axisPermutations[p];
				anyStateTransitions.Add(
					GenerateTransition("", canTransitionToSelf: true, conditions:
						Enumerable.Range(0, axis.Length).Select(x =>
								GenerateCondition(perm[x] ? AnimatorConditionMode.Less : AnimatorConditionMode.Greater,
									$"CustomObjectSync/AngleSign{axis[x]}_Angle", perm[x] ? 0.5000001f : 0.5f))
							.Append(GenerateCondition(AnimatorConditionMode.If, "IsLocal", 0f))
							.Append(GenerateCondition(AnimatorConditionMode.IfNot, "CustomObjectSync/SetStage", 0f)).ToArray()
						, destinationState: displayStates[2 * p].state)
				);
				anyStateTransitions.Add(
					GenerateTransition("", canTransitionToSelf: true, conditions:
						Enumerable.Range(0, axis.Length).SelectMany(x => new []
							{
								GenerateCondition(AnimatorConditionMode.Greater,
									$"CustomObjectSync/Position{axis[x]}{(perm[x] ? "Pos" : "Neg")}", 0),
								GenerateCondition(AnimatorConditionMode.Less,
									$"CustomObjectSync/Position{axis[x]}{(!perm[x] ? "Pos" : "Neg")}", 0.00000001f),
							})
							.Append(GenerateCondition(AnimatorConditionMode.If, "IsLocal", 0f))
							.Append(GenerateCondition(AnimatorConditionMode.If, "CustomObjectSync/SetStage", 0f)).ToArray()
						, destinationState: displayStates[2 * p + 1].state)
				);
			}


			anyStateTransitions.Add(GenerateTransition("", conditions:
					new [] { GenerateCondition(AnimatorConditionMode.If, "IsLocal", 0f),
					GenerateCondition(AnimatorConditionMode.If, "CustomObjectSync/SetStage", 0f)}
				, destinationState: ContactTimeoutState.state, hasExitTime: true, exitTime: 1f));

			displayStateMachine.anyStateTransitions = anyStateTransitions.ToArray().Reverse().ToArray();
			
			AnimatorControllerLayer displayLayer = GenerateLayer("CustomObjectSync/Parameter Display", displayStateMachine);
			return displayLayer;
		}

		private BlendTree GenerateSetterTree(bool isDebugTree)
		{
			BlendTree tree = GenerateBlendTree("RemoteTree", BlendTreeType.Direct);

			BlendTree[] localTrees = axis.Select(x =>
			{
				AnimationClip rotationXMin = GenerateClip($"Rotation{x}Min");
				AddCurve(rotationXMin, "Custom Object Sync/Set/Result", typeof(VRCRotationConstraint), $"RotationOffset.{x.ToLower()}", AnimationCurve.Constant(0, 1/60f, isDebugTree ? -180 : -180 - (360 / (float)Math.Pow(2, rotationPrecision)))) ;
				AnimationClip rotationXMax = GenerateClip($"Rotation{x}Max");
				AddCurve(rotationXMax, "Custom Object Sync/Set/Result", typeof(VRCRotationConstraint), $"RotationOffset.{x.ToLower()}", AnimationCurve.Constant(0, 1/60f, isDebugTree ? 180 : 180 - (360 / (float)Math.Pow(2, rotationPrecision))));
				BlendTree rotationTree = GenerateBlendTree($"Rotation{x}", BlendTreeType.Simple1D,
					blendParameter: isDebugTree ? $"CustomObjectSync/LocalDebugView/Rotation{x}" : $"CustomObjectSync/Rotation{x}");
				rotationTree.children = new ChildMotion[]
				{
					GenerateChildMotion(motion: rotationXMin),
					GenerateChildMotion(motion: rotationXMax)
				};
				return rotationTree;
			}).ToArray();
			
			
			if (quickSync)
			{
				localTrees = localTrees.Concat(axis.Select(x =>
				{
					float value = Mathf.Pow(2, maxRadius);
					float offset = 1 / Mathf.Pow(2, positionPrecision + 1);
					AnimationClip translationMin = GenerateClip($"Translation{x}Min");
					AddCurve(translationMin, "Custom Object Sync/Set/Result", typeof(VRCPositionConstraint), $"PositionOffset.{x.ToLower()}", AnimationCurve.Constant(0, 1/60f,  -offset - value));
					AnimationClip translationZeroNeg = GenerateClip($"Translation{x}ZeroNeg");
					AddCurve(translationZeroNeg, "Custom Object Sync/Set/Result", typeof(VRCPositionConstraint), $"PositionOffset.{x.ToLower()}", AnimationCurve.Constant(0, 1/60f, -offset));
					AnimationClip translationZeroPos = GenerateClip($"Translation{x}ZeroPos");
					AddCurve(translationZeroPos, "Custom Object Sync/Set/Result", typeof(VRCPositionConstraint), $"PositionOffset.{x.ToLower()}", AnimationCurve.Constant(0, 1/60f, offset));
					AnimationClip translationMax = GenerateClip($"Translation{x}Max");
					AddCurve(translationMax, "Custom Object Sync/Set/Result", typeof(VRCPositionConstraint), $"PositionOffset.{x.ToLower()}", AnimationCurve.Constant(0, 1/60f, offset + value));
					BlendTree translationTree = GenerateBlendTree("TranslationX", BlendTreeType.Simple1D, blendParameter: isDebugTree ? $"CustomObjectSync/LocalDebugView/PositionSign{x}" : $"CustomObjectSync/PositionSign{x}");
					
					BlendTree translationMinTree = GenerateBlendTree($"Translation{x}MinTree", blendType: BlendTreeType.Simple1D, blendParameter:isDebugTree ? $"CustomObjectSync/LocalDebugView/Position{x}" : $"CustomObjectSync/Position{x}");
					BlendTree translationMaxTree = GenerateBlendTree($"Translation{x}MaxTree", blendType: BlendTreeType.Simple1D, blendParameter:isDebugTree ? $"CustomObjectSync/LocalDebugView/Position{x}" : $"CustomObjectSync/Position{x}");
					translationMinTree.children = new[]
					{
						GenerateChildMotion(motion: translationZeroNeg),
						GenerateChildMotion(motion: translationMin)
					};
					translationMaxTree.children = new[]
					{
						GenerateChildMotion(motion: translationZeroPos),
						GenerateChildMotion(motion: translationMax)
					};
					translationTree.children = new ChildMotion[]
					{
						GenerateChildMotion(translationMinTree),
						GenerateChildMotion(translationMaxTree)
					};
					return translationTree;
				})).ToArray();
			}
			else
			{
				localTrees = localTrees.Concat(axis.Select(x =>
				{
					AnimationClip translationMin = GenerateClip($"Translation{x}Min");
					AddCurve(translationMin, "Custom Object Sync/Set/Result", typeof(VRCPositionConstraint), $"PositionOffset.{x.ToLower()}", AnimationCurve.Constant(0, 1/60f, -Mathf.Pow(2, maxRadius)));
					AnimationClip translationMax = GenerateClip($"Translation{x}Max");
					AddCurve(translationMax, "Custom Object Sync/Set/Result", typeof(VRCPositionConstraint), $"PositionOffset.{x.ToLower()}", AnimationCurve.Constant(0, 1/60f, Mathf.Pow(2, maxRadius)));
					BlendTree translationTree = GenerateBlendTree($"Translation{x}", BlendTreeType.Simple1D,
						blendParameter: isDebugTree ? $"CustomObjectSync/LocalDebugView/Position{x}" : $"CustomObjectSync/Position{x}");
					translationTree.children = new ChildMotion[]
					{
						GenerateChildMotion(motion: translationMin),
						GenerateChildMotion(motion: translationMax)
					};
					return translationTree;
				})).ToArray();
			}
			
			tree.children = localTrees.Select(x => GenerateChildMotion(motion: x, directBlendParameter: "CustomObjectSync/One")).ToArray();
			return tree;
		}

		private AnimatorControllerLayer SetupLocalDebugViewLayer(VRCAvatarDescriptor descriptor, int objectCount,
			GameObject syncSystem, AnimationClip buffer, int objectParameterCount, bool[][] objectPerms)
		{
			List<ChildAnimatorState> states = new List<ChildAnimatorState>();
			AnimatorState StateIdleRemote = GenerateState("Idle Remote", motion: buffer);
			ChildAnimatorState IdleRemote = GenerateChildState(new Vector3(30f, 200f, 0f), StateIdleRemote);
			states.Add(IdleRemote);
			
			string[] targetStrings = syncObjects.Select(x => AnimationUtility.CalculateTransformPath(addDampeningConstraint ? x.transform.parent.Find($"{x.name} Damping Sync") : x.transform, descriptor.transform)).ToArray();
			string[] dampingConstraints = syncObjects.Select(x => AnimationUtility.CalculateTransformPath(x.transform, descriptor.transform)).ToArray();
			
			AnimationClip idleLocal = GenerateClip("IdleLocal");
			Enumerable.Range(0, targetStrings.Length).ToList().ForEach(i =>
			{
				string targetString = targetStrings[i];
				AnimationCurve enabledCurve = AnimationCurve.Constant(0, 1/60f, 1);
				AddCurve(idleLocal, targetString, typeof(VRCParentConstraint), "m_Enabled",enabledCurve);
				AddCurve(idleLocal, targetString, typeof(VRCParentConstraint), "Sources.source0.Weight", AnimationCurve.Constant(0, 1/60f, 1));
				AddCurve(idleLocal, targetString, typeof(VRCParentConstraint), "Sources.source1.Weight", AnimationCurve.Constant(0, 1/60f, 0));
				if (addDampeningConstraint)
				{
					AddCurve(idleLocal, dampingConstraints[i], typeof(VRCParentConstraint), "Sources.source0.Weight", AnimationCurve.Constant(0, 1/60f, 1));
					AddCurve(idleLocal, dampingConstraints[i], typeof(VRCParentConstraint), "Sources.source1.Weight", AnimationCurve.Constant(0, 1/60f, 0));
				}
			});
			
			AnimatorState StateIdleLocal = GenerateState("Idle Local", motion: idleLocal);
			ChildAnimatorState IdleLocal = GenerateChildState(new Vector3(30f, -80f, 0f), StateIdleLocal);
			states.Add(IdleLocal);
			
			AnimationClip[] constraintsEnabled = Enumerable.Range(0, targetStrings.Length).Select(i =>
			{
				string targetString = targetStrings[i];
				AnimationClip remoteVRCParentConstraintOn = GenerateClip($"remoteVRCParentConstraintEnabled{i}");
				AnimationCurve enabledCurve = quickSync ? new AnimationCurve(new Keyframe(0, 0),  new Keyframe(6 / 60f, 0),  new Keyframe(7 / 60f, 1), new Keyframe(8 / 60f, 0)) : 
					new AnimationCurve(new Keyframe(0, 0), new Keyframe(1 / 60f, 1), new Keyframe(2 / 60f, 0));
				AddCurve(remoteVRCParentConstraintOn, targetString, typeof(VRCParentConstraint), "m_Enabled",enabledCurve);
				AddCurve(remoteVRCParentConstraintOn, targetString, typeof(VRCParentConstraint), "Sources.source0.Weight", AnimationCurve.Constant(0, 1/60f, 0));
				AddCurve(remoteVRCParentConstraintOn, targetString, typeof(VRCParentConstraint), "Sources.source1.Weight", AnimationCurve.Constant(0, 1/60f, 1));
				if (addDampeningConstraint)
				{
					AddCurve(remoteVRCParentConstraintOn, dampingConstraints[i], typeof(VRCParentConstraint), "Sources.source0.Weight", AnimationCurve.Constant(0, 1/60f, dampingConstraintValue));
					AddCurve(remoteVRCParentConstraintOn, dampingConstraints[i], typeof(VRCParentConstraint), "Sources.source1.Weight", AnimationCurve.Constant(0, 1/60f, 1));
				}
				return remoteVRCParentConstraintOn;
			}).ToArray();
			AnimationClip[] constraintsDisabled = Enumerable.Range(0, targetStrings.Length).Select(i =>
			{
				string targetString = targetStrings[i];
				AnimationClip remoteVRCParentConstraintOffAnim = GenerateClip($"remoteVRCParentConstraintDisabled{i}");
				AddCurve(remoteVRCParentConstraintOffAnim, targetString, typeof(VRCParentConstraint), "m_Enabled", AnimationCurve.Constant(0, 1/60f, 0));
				AddCurve(remoteVRCParentConstraintOffAnim, targetString, typeof(VRCParentConstraint), "Sources.source0.Weight", AnimationCurve.Constant(0, 1/60f, 0));
				AddCurve(remoteVRCParentConstraintOffAnim, targetString, typeof(VRCParentConstraint), "Sources.source1.Weight", AnimationCurve.Constant(0, 1/60f, 1));
				if (addDampeningConstraint)
				{
					AddCurve(remoteVRCParentConstraintOffAnim, dampingConstraints[i], typeof(VRCParentConstraint), "Sources.source0.Weight", AnimationCurve.Constant(0, 1/60f, dampingConstraintValue));
					AddCurve(remoteVRCParentConstraintOffAnim, dampingConstraints[i], typeof(VRCParentConstraint), "Sources.source1.Weight", AnimationCurve.Constant(0, 1/60f, 1));
				}
				return remoteVRCParentConstraintOffAnim;
			}).ToArray();
			
			for (int i = 0; i < objectCount; i++)
			{
				BlendTree setPositionTree = GenerateSetterTree(true);
				BlendTree localTree = GenerateBlendTree($"LocalObject{i}", BlendTreeType.Direct);
				localTree.children = Enumerable.Range(0, objectCount)
					.Select(i2 =>
					{
						return GenerateChildMotion(motion: ((i2 + 1) % objectCount) == i ? constraintsEnabled[i2] : constraintsDisabled[i2], directBlendParameter: "CustomObjectSync/One");
					}).Append(GenerateChildMotion(motion: setPositionTree, directBlendParameter: "CustomObjectSync/One")).ToArray();
				
				AnimatorState localState = GenerateState($"Local Object {i}", motion: localTree, writeDefaultValues: true);
				localState.behaviours = new StateMachineBehaviour[] {
					GenerateParameterDriver(axis.SelectMany(x =>
					{
						List<Parameter> parameters = new List<Parameter>();


						if (quickSync)
						{
							float multiplier = (float)Math.Pow(2, maxRadius + positionPrecision - 1);
							Parameter pos1 = GenerateParameter(ChangeType.Copy, name: $"CustomObjectSync/LocalDebugView/PositionTmp2{x}", source: $"CustomObjectSync/Position{x}", convertRange: true, sourceMin: -1, sourceMax: 1, destMin: -multiplier, destMax: multiplier);
							Parameter pos2 = GenerateParameter(ChangeType.Add, name: $"CustomObjectSync/LocalDebugView/PositionTmp2{x}", value: 0.5f);
							Parameter pos3 = GenerateParameter(ChangeType.Copy, name: $"CustomObjectSync/LocalDebugView/PositionTmp{x}", source: $"CustomObjectSync/LocalDebugView/PositionTmp2{x}");
							Parameter pos4 = GenerateParameter(ChangeType.Copy, name: $"CustomObjectSync/LocalDebugView/Position{x}", source: $"CustomObjectSync/LocalDebugView/PositionTmp{x}", convertRange: true, sourceMin: -multiplier, sourceMax: multiplier, destMin: -1, destMax: 1);
							Parameter pos5 = GenerateParameter(ChangeType.Copy, name: $"CustomObjectSync/LocalDebugView/PositionSign{x}", source: $"CustomObjectSync/PositionSign{x}");
							parameters.Add(pos1);
							parameters.Add(pos2);
							parameters.Add(pos3);
							parameters.Add(pos4);
							parameters.Add(pos5);
						}
						else
						{
							float multiplier = (float)Math.Pow(2, maxRadius + positionPrecision);
							Parameter pos1 = GenerateParameter(ChangeType.Copy, name: $"CustomObjectSync/LocalDebugView/PositionTmp{x}", source: $"CustomObjectSync/Position{x}", convertRange: true, sourceMin: -1, sourceMax: 1, destMin: -multiplier, destMax: multiplier);
							Parameter pos2 = GenerateParameter(ChangeType.Copy, name: $"CustomObjectSync/LocalDebugView/Position{x}", source: $"CustomObjectSync/LocalDebugView/PositionTmp{x}", convertRange: true, sourceMin: -multiplier, sourceMax: multiplier, destMin: -1, destMax: 1);
							parameters.Add(pos1);
							parameters.Add(pos2);
						}
						if (rotationEnabled)
						{
							Parameter rot1 = GenerateParameter(ChangeType.Copy, name: $"CustomObjectSync/LocalDebugView/RotationTmp{x}", source: $"CustomObjectSync/Rotation{x}", convertRange: true, sourceMin: -1, sourceMax: 1, destMin: -(float)Math.Pow(2, rotationPrecision), destMax: (float)Math.Pow(2, rotationPrecision));
							Parameter rot2 = GenerateParameter(ChangeType.Copy, name: $"CustomObjectSync/LocalDebugView/Rotation{x}", source: $"CustomObjectSync/LocalDebugView/RotationTmp{x}", convertRange: true, sourceMin: -(float)Math.Pow(2, rotationPrecision), sourceMax: (float)Math.Pow(2, rotationPrecision),  destMin: -1, destMax: 1);
							parameters.Add(rot1);
							parameters.Add(rot2);
						}
						return parameters.ToArray();
					}).ToArray())		
				};
				ChildAnimatorState localChildState = GenerateChildState(new Vector3(30f -  ((objectCount - 1)/2f * 300f) + i * 300,  -180f, 0f), localState);
				states.Add(localChildState);
			}

			AnimatorStateTransition[] anyStateTransitions = Enumerable.Range(0, objectCount).Select(i =>
			{
				AnimatorStateTransition transition = GenerateTransition("", conditions:
					Enumerable.Range(0, objectParameterCount).Select(x => GenerateCondition(objectPerms[i][x] ? AnimatorConditionMode.Greater : AnimatorConditionMode.Less, $"CustomObjectSync/LocalReadBit{x}", threshold: 0.5f))
						.Append(GenerateCondition(AnimatorConditionMode.If, "IsLocal", 0f))
						.Append(GenerateCondition(AnimatorConditionMode.If, "CustomObjectSync/Enabled", 0f))
						.Append(GenerateCondition(AnimatorConditionMode.If, "CustomObjectSync/LocalDebugView", 0f)).ToArray()
					, destinationState: states[i+2].state);
				if (objectCount == 1)
				{
					transition.canTransitionToSelf = true;

					if (!quickSync)
					{
						transition.conditions = transition.conditions.Append(GenerateCondition(AnimatorConditionMode.If, "CustomObjectSync/StartRead/0", 0f)).Append(GenerateCondition(AnimatorConditionMode.IfNot, "CustomObjectSync/ReadInProgress/0", 0f)).ToArray();
					}
					else
					{
						BlendTree stateTree = states[i + 2].state.motion as BlendTree;
						AnimationClip lengthBuffer = GenerateClip("LengthDebugBuffer");
						AddCurve(lengthBuffer, "THIS OBJECT DOES NOT EXIST", typeof(GameObject), "m_Enabled", AnimationCurve.Constant(0, 12/60f, 1));
						stateTree.children = stateTree.children.Append(GenerateChildMotion(motion: lengthBuffer, directBlendParameter: "CustomObjectSync/One")).ToArray();
						transition.hasExitTime = true;
						transition.exitTime = 1f;
					}
				}
				
				return transition;
			}).Append(GenerateTransition("", conditions: new []
			{
				GenerateCondition(AnimatorConditionMode.If, "IsLocal", 0f),
				GenerateCondition(AnimatorConditionMode.IfNot, "CustomObjectSync/LocalDebugView", 0f)
			}, destinationState: IdleLocal.state))
			.Append(GenerateTransition("", conditions: new []
			{
				GenerateCondition(AnimatorConditionMode.If, "IsLocal", 0f),
				GenerateCondition(AnimatorConditionMode.IfNot, "CustomObjectSync/Enabled", 0f)
			}, destinationState: IdleLocal.state))
			.Append(GenerateTransition("", conditions: new []
			{
				GenerateCondition(AnimatorConditionMode.IfNot, "IsLocal", 0f),
			}, destinationState: IdleRemote.state)).ToArray();
			
			AnimatorStateMachine debugStateMachine = GenerateStateMachine("CustomObjectSync/Local Debug View", new Vector3(50f, 20f, 0f), new Vector3(50f, 120f, 0f), new Vector3(800f, 120f, 0f), states: states.ToArray(), defaultState: IdleRemote.state, anyStateTransitions: anyStateTransitions);

			AnimatorControllerLayer localDebugViewLayer = GenerateLayer("CustomObjectSync/Local Debug View", debugStateMachine);
			return localDebugViewLayer;
		}
		
		// From https://stackoverflow.com/questions/38069770/add-one-gameobject-component-into-another-gameobject-with-values-at-runtime
		public static T CopyValues<T>(Component target, T source) where T : Component
		{
			Type type = target.GetType();
			if (type != source.GetType()) return null; // type mis-match
			while (type != null && type != typeof(Behaviour))
			{
				BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance |
				                     BindingFlags.Default | BindingFlags.DeclaredOnly;
				PropertyInfo[] pinfos = type.GetProperties(flags);
				foreach (var pinfo in pinfos)
				{
					if (pinfo.CanWrite)
					{
						try
						{
							pinfo.SetValue(target, pinfo.GetValue(source, null), null);
						}
						catch
						{
						} // In case of NotImplementedException being thrown.
					}
				}

				FieldInfo[] finfos = type.GetFields(flags);
				foreach (var finfo in finfos)
				{
					finfo.SetValue(target, finfo.GetValue(source));
				}

				type = type.BaseType;
			}

			return target as T;
		}
        
		private void MoveConstraint(IConstraint constraint, GameObject targetObject)
		{
			IConstraint newConstraint = (IConstraint)targetObject.AddComponent(constraint.GetType());
			CopyValues((Component)newConstraint, (Component)constraint);
			for (var i = 0; i < constraint.sourceCount; i++)
			{
				var source = constraint.GetSource(i);
				var newSource = new ConstraintSource();
				newSource.sourceTransform = source.sourceTransform;
				newSource.weight = source.weight;
				newConstraint.AddSource(newSource);
			}
			DestroyImmediate((Component)constraint);
		}
		
		private void MoveVRChatConstraint(VRCConstraintBase constraint, GameObject targetObject)
		{
			VRCConstraintBase newConstraint = (VRCConstraintBase)targetObject.AddComponent(constraint.GetType());
			CopyValues((Component)newConstraint, (Component)constraint);
			DestroyImmediate((Component)constraint);
		}

		private GameObject InstallSystem(VRCAvatarDescriptor descriptor, AnimatorController mergedController,
			VRCExpressionParameters parameterObject)
		{
			GameObject rootObject = descriptor.gameObject;
			GameObject syncSystem = Instantiate(resourcePrefab, rootObject.transform);
			syncSystem.name = syncSystem.name.Replace("(Clone)", "");
			if (!rotationEnabled)
			{
				DestroyImmediate(syncSystem.transform.Find("Measure/Rotation").gameObject);
				DestroyImmediate(syncSystem.transform.Find("Set/Result").GetComponent<RotationConstraint>());
			}

			foreach (string s in axis)
			{
				Transform sender = syncSystem.transform.Find($"Measure/Position/Sender{s}");
				float radius = Mathf.Pow(2, maxRadius);
				VRCPositionConstraint sendConstraint = sender.GetComponent<VRCPositionConstraint>();
				sendConstraint.PositionAtRest = new Vector3(0, 0, 0);
				VRCConstraintSource source0 = sendConstraint.Sources[0];
				source0.Weight = 1 - (3f / (radius));	
				sendConstraint.Sources[0] = source0;
				VRCConstraintSource source1 = sendConstraint.Sources[1];
				source1.Weight = 3f / (radius);
				sendConstraint.Sources[1] = source1;
			}

			Transform mainTargetObject = syncSystem.transform.Find("Target");
			VRCParentConstraint mainTargetVRCParentConstraint = mainTargetObject.gameObject.AddComponent<VRCParentConstraint>();
			mainTargetVRCParentConstraint.Locked = true;
			mainTargetVRCParentConstraint.IsActive = true;
			for (var i = 0; i < syncObjects.Length; i++)
			{
				GameObject targetSyncObject = syncObjects[i];
				Transform targetObject = new GameObject($"{targetSyncObject.name} Target").transform;
				targetObject.parent = targetSyncObject.transform.parent;
				targetObject.localPosition = targetSyncObject.transform.localPosition;
				targetObject.localRotation = targetSyncObject.transform.localRotation;
				targetObject.localScale = targetSyncObject.transform.localScale;
				mainTargetVRCParentConstraint.Sources.Add(new VRCConstraintSource(targetObject, 0f, Vector3.zero, Vector3.zero));
				string oldPath = GetDescriptorPath(targetSyncObject);
				targetSyncObject.transform.parent = syncSystem.transform;
				if (targetSyncObject.name == "Target") targetSyncObject.name = "User Target";
				string newPath = GetDescriptorPath(targetSyncObject);
				AnimationClip[] allClips = descriptor.baseAnimationLayers.Concat(descriptor.specialAnimationLayers)
					.Where(x => x.animatorController != null).SelectMany(x => x.animatorController.animationClips)
					.ToArray();
				RenameClipPaths(allClips, false, oldPath, newPath);
				
				// Unity Constraints
				var components = targetSyncObject.GetComponents<IConstraint>();
				string targetPath = GetDescriptorPath(targetObject);
				for (var j = 0; j < components.Length; j++)
				{
					// Move constraint animations to the new path.
					AnimationClip[] targetClips = allClips.Where(x =>
						AnimationUtility.GetCurveBindings(x)
							.Any(y => y.type == components[j].GetType() && y.path == newPath)).ToArray();
					foreach (AnimationClip animationClip in targetClips)
					{
						EditorCurveBinding[] curveBindings = AnimationUtility.GetCurveBindings(animationClip);
						for (var bi = 0; bi < curveBindings.Length; bi++)
						{
							var curveBinding = curveBindings[bi];
							AnimationCurve curve = AnimationUtility.GetEditorCurve(animationClip, curveBinding);
							AnimationUtility.SetEditorCurve(animationClip, curveBinding, null);
							if (curveBinding.type == components[j].GetType() && curveBinding.path == newPath)
							{
								curveBinding.path = targetPath;
							}
							AnimationUtility.SetEditorCurve(animationClip, curveBinding, curve);
						}
					}
					
					MoveConstraint(components[j], targetObject.gameObject);
				}
				
				// VRChat Constraints
				var vrcComponents = targetSyncObject.GetComponents<VRCConstraintBase>();
				string vrcTargetPath = GetDescriptorPath(targetObject);
				for (var j = 0; j < vrcComponents.Length; j++)
				{
					// Move constraint animations to the new path.
					AnimationClip[] targetClips = allClips.Where(x =>
						AnimationUtility.GetCurveBindings(x)
							.Any(y => y.type == vrcComponents[j].GetType() && y.path == newPath)).ToArray();
					foreach (AnimationClip animationClip in targetClips)
					{
						EditorCurveBinding[] curveBindings = AnimationUtility.GetCurveBindings(animationClip);
						for (var bi = 0; bi < curveBindings.Length; bi++)
						{
							var curveBinding = curveBindings[bi];
							AnimationCurve curve = AnimationUtility.GetEditorCurve(animationClip, curveBinding);
							AnimationUtility.SetEditorCurve(animationClip, curveBinding, null);
							if (curveBinding.type == vrcComponents[j].GetType() && curveBinding.path == newPath)
							{
								curveBinding.path = vrcTargetPath;
							}
							AnimationUtility.SetEditorCurve(animationClip, curveBinding, curve);
						}
					}
					
					MoveVRChatConstraint(vrcComponents[j], targetObject.gameObject);
				}
				
				GameObject damping = null;
				if (addDampeningConstraint)
				{
					damping = new GameObject($"{targetSyncObject.name} Damping Sync");
					damping.transform.parent = syncSystem.transform;
					VRCParentConstraint targetConstraint = targetSyncObject.AddComponent<VRCParentConstraint>();
					targetConstraint.Locked = true;
					targetConstraint.IsActive = true;
					targetConstraint.Sources.Add(new VRCConstraintSource(damping.transform, 1f, Vector3.zero, Vector3.zero));
					targetConstraint.Sources.Add(new VRCConstraintSource(targetSyncObject.transform, 0f, Vector3.zero, Vector3.zero));
				}
				
				VRCParentConstraint containerConstraint = addDampeningConstraint ? damping.AddComponent<VRCParentConstraint>() : targetSyncObject.AddComponent<VRCParentConstraint>();
				containerConstraint.Sources.Add((new VRCConstraintSource(targetObject, 1f, Vector3.zero, Vector3.zero)));
				containerConstraint.Sources.Add((new VRCConstraintSource(syncSystem.transform.Find("Set/Result"), 0f, Vector3.zero, Vector3.zero)));
				containerConstraint.Locked = true;
				containerConstraint.IsActive = true;
				
				VRCScaleConstraint VRCScaleConstraint = targetSyncObject.gameObject.AddComponent<VRCScaleConstraint>();
				VRCScaleConstraint.Sources.Add(new VRCConstraintSource(targetObject, 1f, Vector3.zero, Vector3.zero));
				VRCScaleConstraint.Locked = true;
				VRCScaleConstraint.IsActive = true;
			}
			Transform setTransform = syncSystem.transform.Find("Set");
			Transform measureTransform = syncSystem.transform.Find("Measure");
			float contactBugOffset = Mathf.Pow(2, maxRadius - 6); // Fixes a bug where proximity contacts near edges give 0, so we set this theoretical 0 to far away from spawn to reduce chances of this happening


			Transform contactx = measureTransform.Find("Position/SenderX");
			contactx.GetComponent<VRCPositionConstraint>().PositionOffset = new Vector3(contactBugOffset / Mathf.Pow(2, maxRadius) * 3, 0, 0);
			Transform contacty = measureTransform.Find("Position/SenderY");
			contacty.GetComponent<VRCPositionConstraint>().PositionOffset = new Vector3(0, contactBugOffset / Mathf.Pow(2, maxRadius) * 3, 0);
			Transform contactz = measureTransform.Find("Position/SenderZ");
			contactz.GetComponent<VRCPositionConstraint>().PositionOffset = new Vector3(0, 0, contactBugOffset / Mathf.Pow(2, maxRadius) * 3);

			
			setTransform.localPosition = new Vector3(-contactBugOffset, -contactBugOffset, -contactBugOffset);
			if (centeredOnAvatar || quickSync)
			{
				VRCPositionConstraint setConstraint = setTransform.gameObject.AddComponent<VRCPositionConstraint>();
				VRCPositionConstraint measureConstraint = measureTransform.gameObject.AddComponent<VRCPositionConstraint>();
				setConstraint.Sources.Add(new VRCConstraintSource(descriptor.transform, 1f, Vector3.zero, Vector3.zero));
				measureConstraint.Sources.Add(new VRCConstraintSource(descriptor.transform, 1f, Vector3.zero, Vector3.zero));
				setConstraint.PositionAtRest = Vector3.zero;
				setConstraint.PositionOffset = new Vector3(-contactBugOffset, -contactBugOffset, -contactBugOffset);
				setConstraint.Locked = true;
				setConstraint.IsActive = true;
				measureConstraint.PositionAtRest = Vector3.zero;
				measureConstraint.PositionOffset = Vector3.zero;
				measureConstraint.Locked = true;
				measureConstraint.IsActive = true;
			}
			
			VRCAvatarDescriptor.CustomAnimLayer[] layers = descriptor.baseAnimationLayers;
			int fxLayerIndex = layers.ToList().FindIndex(x => x.type == VRCAvatarDescriptor.AnimLayerType.FX);
			VRCAvatarDescriptor.CustomAnimLayer fxLayer = layers[fxLayerIndex];
			fxLayer.isDefault = false;
			fxLayer.animatorController = mergedController;
			layers[fxLayerIndex] = fxLayer;
			descriptor.baseAnimationLayers = layers;

			descriptor.customExpressions = true;
			descriptor.expressionParameters = parameterObject;
			
			Selection.activeObject = descriptor.gameObject;
			return syncSystem;
		}

		private AnimatorControllerLayer SetupSyncLayer(int syncSteps, int positionBits, int rotationBits, int objectCount,
			int objectParameterCount, bool[][] objectPerms, int syncStepParameterCount, bool[][] syncStepPerms)
		{
			AnimatorStateMachine syncMachine = GenerateStateMachine("CustomObjectSync/Sync", new Vector3(-80, 0, 0), new Vector3(-80, 200 , 0), new Vector3(-80, 100, 0));
			AnimatorControllerLayer syncLayer = GenerateLayer("CustomObjectSync/Sync", syncMachine);

			AnimationClip bufferWaitInit = GenerateClip($"BufferWait{(int)(syncSteps*1.5f)}");
			AddCurve(bufferWaitInit, "Custom Object Sync/Measure", typeof(VRCPositionConstraint), "m_Enabled", AnimationCurve.Constant(0, ((Math.Max(positionBits, rotationBits)*1.5f))/60f, 0));
			AddCurve(bufferWaitInit, "Custom Object Sync/Set", typeof(VRCPositionConstraint), "m_Enabled", AnimationCurve.Constant(0, ((Math.Max(positionBits, rotationBits)*1.5f))/60f, 0));

			AnimationClip bufferWaitSync = GenerateClip($"BufferWaitSync");
			AddCurve(bufferWaitSync, "Custom Object Sync/Measure", typeof(VRCPositionConstraint), "m_Enabled", AnimationCurve.Constant(0, 12/60f, 0));
			AddCurve(bufferWaitSync, "Custom Object Sync/Set", typeof(VRCPositionConstraint), "m_Enabled", AnimationCurve.Constant(0, 12/60f, 0));
			
			AnimationClip enableWorldConstraint = GenerateClip($"EnableWorldConstraint");
			AddCurve(enableWorldConstraint, "Custom Object Sync/Measure", typeof(VRCPositionConstraint), "m_Enabled", AnimationCurve.Constant(0, 12/60f, 1));
			AddCurve(enableWorldConstraint, "Custom Object Sync/Set", typeof(VRCPositionConstraint), "m_Enabled", AnimationCurve.Constant(0, 12/60f, 1));
			
			#region SyncStates
			ChildAnimatorState initState = GenerateChildState(new Vector3(-100, -150, 0), GenerateState("SyncInit", motion: enableWorldConstraint));
			List<ChildAnimatorState> localStates = new List<ChildAnimatorState>();
			List<ChildAnimatorState> remoteStates = new List<ChildAnimatorState>();
			if (quickSync)
			{
				string[] syncParameterNames = axis.Select(x => $"Position{x}").Concat(axis.Select(x => $"PositionSign{x}")).ToArray();
				if (rotationEnabled)
				{
					syncParameterNames = syncParameterNames.Concat(axis.Select(x => $"Rotation{x}")).ToArray();
				}
				for (int o = 0; o < objectCount; o++)
				{
					localStates.Add(GenerateChildState(new Vector3(-500,-(objectCount) * 25 + ((o + 1) * 50), 0), GenerateState($"SyncLocal{o}", motion: bufferWaitSync)));
					localStates.Add(GenerateChildState(new Vector3(-800, -(objectCount) * 25 + ((o + 1) * 50), 0), GenerateState($"SyncLocal{o}Buffer", motion: bufferWaitSync)));
					localStates.Last().state.behaviours = new[]
					{
						GenerateParameterDriver(Enumerable.Range(0, objectParameterCount).Select(x => GenerateParameter(ChangeType.Set, name: $"CustomObjectSync/Sync/Object{x}", value: objectPerms[(o + 1) % objectCount][x] ? 1 : 0)).ToArray()),
						GenerateParameterDriver(syncParameterNames.Select(x => GenerateParameter(ChangeType.Copy, source: $"CustomObjectSync/{x}", name: $"CustomObjectSync/Sync/{x}")).ToArray()),
						GenerateParameterDriver(Enumerable.Range(0, objectParameterCount).Select(x => GenerateParameter(ChangeType.Set, name: $"CustomObjectSync/LocalReadBit{x}", value: objectPerms[(o + 1) % objectCount][x] ? 1f : 0f)).ToArray())
					};
					
					remoteStates.Add(GenerateChildState(new Vector3(300, -(objectCount) * 25 + ((o + 1) * 50), 0), GenerateState($"SyncRemote{o}")));
					remoteStates.Last().state.behaviours = new[]
					{
						GenerateParameterDriver(syncParameterNames.Select(x => GenerateParameter(ChangeType.Copy, source: $"CustomObjectSync/Sync/{x}", name: $"CustomObjectSync/{x}")).ToArray())
					};
				}
			}
			else
			{
				string[] syncParameterNames = axis
				.SelectMany(n => Enumerable.Range(0, positionBits).Select(i => $"CustomObjectSync/Bits/Copy/Position{n}{i}"))
				.Concat(axis.SelectMany(n =>
					Enumerable.Range(0, rotationBits).Select(i => $"CustomObjectSync/Bits/Copy/Rotation{n}{i}"))).ToArray();
				string[][] localSyncParameterNames = Enumerable.Range(0, objectCount).Select(o => axis
					.SelectMany(n => Enumerable.Range(0, positionBits).Select(i => $"CustomObjectSync/Bits/Position{n}{i}/{o}"))
					.Concat(axis.SelectMany(n =>
						Enumerable.Range(0, rotationBits).Select(i => $"CustomObjectSync/Bits/Rotation{n}{i}/{o}"))).ToArray()).ToArray();

				int stepToStartSync = Mathf.CeilToInt(Math.Max(positionBits, rotationBits)*1.5f / 12f);
				bool shouldDelayFirst = (stepToStartSync > syncSteps);

				if (shouldDelayFirst)
				{
					stepToStartSync = syncSteps;
				}
				

				int totalSyncSteps = objectCount * syncSteps;
				
				for (int i = 0; i < totalSyncSteps; i++)
				{
					int o = i / syncSteps;
					int s = i % syncSteps;
					localStates.Add(GenerateChildState(new Vector3(-500,-(totalSyncSteps) * 25 + ((i + 1) * 50), 0), GenerateState($"SyncLocal{i}", motion: bufferWaitSync)));
					if (shouldDelayFirst && i % syncSteps == 0)
					{
						localStates.Last().state.motion = bufferWaitInit;
					}
					
					if (i % syncSteps == syncSteps - 1)
					{
						// When we begin sending copy out values so we have them ready to send
						localStates.Last().state.behaviours = localStates.Last().state.behaviours
							.Append(GenerateParameterDriver(Enumerable.Range(0, syncParameterNames.Length).Select(x => GenerateParameter(ChangeType.Copy, localSyncParameterNames[(o + 1) % objectCount][x], syncParameterNames[x])).ToArray())).ToArray(); 
					}
					
					if (syncSteps - s == stepToStartSync)
					{
						localStates.Last().state.behaviours = localStates.Last().state.behaviours
							.Append(GenerateParameterDriver(
								Enumerable.Range(0, objectParameterCount).Select(x => GenerateParameter(ChangeType.Set, name: $"CustomObjectSync/LocalReadBit{x}", value: objectPerms[(o + 1) % objectCount][x] ? 1f : 0f)).Append(
									GenerateParameter(ChangeType.Set, name: $"CustomObjectSync/StartRead/{(o + 1) % objectCount}", value: 1)).ToArray()))
							.ToArray();
					}

					
					localStates.Add(GenerateChildState(new Vector3(-800, -(totalSyncSteps) * 25 + ((i + 1) * 50), 0), GenerateState($"SyncLocal{i}Buffer", motion: bufferWaitSync)));
					localStates.Last().state.behaviours = new[]
					{
						GenerateParameterDriver(Enumerable.Range(0, syncStepParameterCount).Select(x => GenerateParameter(ChangeType.Set, name: $"CustomObjectSync/Sync/Step{x}", value: syncStepPerms[(i + 1) % (syncSteps)][x] ? 1 : 0)).ToArray()),
						GenerateParameterDriver(Enumerable.Range(0, objectParameterCount).Select(x => GenerateParameter(ChangeType.Set, name: $"CustomObjectSync/Sync/Object{x}", value: objectPerms[((i + 1) % (totalSyncSteps))/ syncSteps][x] ? 1 : 0)).ToArray()),
						GenerateParameterDriver(
							Enumerable
								.Range(((s + 1) % syncSteps) * bitCount, Math.Min((((s + 1) % syncSteps) + 1) * bitCount, GetMaxBitCount()) - ((((s + 1) % syncSteps)) * bitCount))
								.Select(x => GenerateParameter(ChangeType.Copy, source: syncParameterNames[x],
									name: $"CustomObjectSync/Sync/Data{x % bitCount}"))
								.ToArray())
					};
				}
				
				for (int i = 0; i < totalSyncSteps; i++)
				{
					int o = i / syncSteps;
					int s = i % syncSteps;
					remoteStates.Add(GenerateChildState(new Vector3(300, -(totalSyncSteps) * 25 + ((i + 1) * 50), 0), GenerateState($"SyncRemote{i}", motion: bufferWaitSync)));
					remoteStates.Last().state.behaviours = new[]
					{
						GenerateParameterDriver(
							Enumerable
								.Range(s * bitCount, Math.Min((s + 1) * bitCount, GetMaxBitCount()) - (s * bitCount))
								.Select(x => GenerateParameter(ChangeType.Copy, source: $"CustomObjectSync/Sync/Data{x % bitCount}", name: syncParameterNames[x]))
								.ToArray())
					};
					if (s == syncSteps - 1)
					{
						remoteStates.Last().state.behaviours = 
							remoteStates.Last().state.behaviours.Append(GenerateParameterDriver(new [] {GenerateParameter(ChangeType.Set, name: $"CustomObjectSync/StartWrite/{o}", value: 1)})).ToArray();
						remoteStates.Last().state.behaviours = remoteStates.Last().state.behaviours
							.Append(GenerateParameterDriver(Enumerable.Range(0, syncParameterNames.Length).Select(x => GenerateParameter(ChangeType.Copy, syncParameterNames[x], localSyncParameterNames[o][x])).ToArray())).ToArray();
					}
				}
			}
			#endregion
			
			#region SyncTransitions
			
			List<AnimatorStateTransition> syncAnyStateTransitions = new List<AnimatorStateTransition>();
			
			syncAnyStateTransitions.Add(GenerateTransition("", conditions: new [] {GenerateCondition(AnimatorConditionMode.IfNot, "CustomObjectSync/Enabled", 0)}, destinationState: initState.state));

			if (quickSync)
			{
				for (int i = 0; i < localStates.Count/2; i++)
				{ 
					syncAnyStateTransitions.Add(GenerateTransition("",  conditions:
						Enumerable.Range(0, objectParameterCount).Select(x => GenerateCondition(objectPerms[i][x] ? AnimatorConditionMode.If : AnimatorConditionMode.IfNot, $"CustomObjectSync/Sync/Object{x}", threshold: 0f))
						.Append(GenerateCondition(AnimatorConditionMode.If, "IsLocal", 0))
						.Append(GenerateCondition(AnimatorConditionMode.If, "CustomObjectSync/Enabled", 0)).ToArray(), destinationState: localStates[i * 2].state));

					localStates[i * 2].state.transitions = new[]
					{
						GenerateTransition("", destinationState: localStates[(i*2)+1].state, hasExitTime: true, exitTime: 1)
					};
				}
			
				for (int i = 0; i < remoteStates.Count; i++)
				{
					int o = i;
					syncAnyStateTransitions.Add(GenerateTransition("", canTransitionToSelf: true, conditions:
						Enumerable.Range(0, objectParameterCount).Select(x => GenerateCondition(objectPerms[o][x] ? AnimatorConditionMode.If : AnimatorConditionMode.IfNot, $"CustomObjectSync/Sync/Object{x}", threshold: 0f))
						.Append(GenerateCondition(AnimatorConditionMode.IfNot, "IsLocal", 0))
						.Append(GenerateCondition(AnimatorConditionMode.If, "CustomObjectSync/Enabled", 0)).ToArray(), destinationState: remoteStates[i].state));
				}
			}
			else
			{
				for (int i = 0; i < localStates.Count/2; i++)
				{
					int o = i / syncSteps;
					syncAnyStateTransitions.Add(GenerateTransition("",  conditions: Enumerable.Range(0, syncStepParameterCount)
						.Select(x => GenerateCondition(syncStepPerms[(i) % (syncSteps)][x] ? AnimatorConditionMode.If : AnimatorConditionMode.IfNot, $"CustomObjectSync/Sync/Step{x}", 0))
						.Concat(Enumerable.Range(0, objectParameterCount).Select(x => GenerateCondition(objectPerms[o][x] ? AnimatorConditionMode.If : AnimatorConditionMode.IfNot, $"CustomObjectSync/Sync/Object{x}", threshold: 0f)))
						.Append(GenerateCondition(AnimatorConditionMode.If, "IsLocal", 0))
						.Append(GenerateCondition(AnimatorConditionMode.If, "CustomObjectSync/Enabled", 0)).ToArray(), destinationState: localStates[i * 2].state));

					localStates[i * 2].state.transitions = new[]
					{
						GenerateTransition("", destinationState: localStates[(i*2)+1].state, hasExitTime: true, exitTime: 1)
					};
				}
			
				for (int i = 0; i < remoteStates.Count; i++)
				{
					int o = i / syncSteps;
					syncAnyStateTransitions.Add(GenerateTransition("", canTransitionToSelf: true, conditions: Enumerable.Range(0, syncStepParameterCount)
						.Select(x => GenerateCondition(syncStepPerms[(i) % (syncSteps)][x] ? AnimatorConditionMode.If : AnimatorConditionMode.IfNot, $"CustomObjectSync/Sync/Step{x}", 0))
						.Concat(Enumerable.Range(0, objectParameterCount).Select(x => GenerateCondition(objectPerms[o][x] ? AnimatorConditionMode.If : AnimatorConditionMode.IfNot, $"CustomObjectSync/Sync/Object{x}", threshold: 0f)))
						.Append(GenerateCondition(AnimatorConditionMode.IfNot, "IsLocal", 0))
						.Append(GenerateCondition(AnimatorConditionMode.If, "CustomObjectSync/Enabled", 0)).ToArray(), destinationState: remoteStates[i].state));
				}	
			}
			
			syncMachine.anyStateTransitions = syncAnyStateTransitions.ToArray();
			#endregion
			
			syncMachine.states = localStates.Concat(remoteStates).Concat(new [] { initState }).ToArray();
			syncMachine.defaultState = initState.state;
			return syncLayer;
		}

		private void SetupSyncLayerParameters(int syncSteps, int objectCount, int objectParameterCount,
			int syncStepParameterCount, bool[][] syncStepPerms, AnimatorController mergedController)
		{
			List<AnimatorControllerParameter> syncParameters = new List<AnimatorControllerParameter>();
			for (int i = 0; i < objectParameterCount; i++)
			{
				syncParameters.Add(GenerateBoolParameter( $"CustomObjectSync/Sync/Object{i}", false));
			}
			if (quickSync)
			{
				syncParameters = syncParameters.Concat(axis.Select(x => GenerateFloatParameter($"CustomObjectSync/Sync/Position{x}"))).ToList();
				syncParameters = syncParameters.Concat(axis.Select(x => GenerateBoolParameter($"CustomObjectSync/Sync/PositionSign{x}"))).ToList();
				if (rotationEnabled)
				{
					syncParameters = syncParameters.Concat(axis.Select(x => GenerateFloatParameter($"CustomObjectSync/Sync/Rotation{x}"))).ToList();
				}
			}
			else
			{
				for (int i = 0; i < bitCount; i++)
				{
					syncParameters.Add(GenerateBoolParameter( $"CustomObjectSync/Sync/Data{i}", false));
				}
				for (int i = 0; i < syncStepParameterCount; i++)
				{
					syncParameters.Add(GenerateBoolParameter( $"CustomObjectSync/Sync/Step{i}", syncStepPerms[syncSteps-1][i]));
				}

				for (int i = 0; i < objectParameterCount; i++)
				{
					syncParameters.Add(GenerateBoolParameter( $"CustomObjectSync/Object{i}", false));
					for (int o = 0; o < objectCount; o++)
					{
						syncParameters.Add(GenerateBoolParameter( $"CustomObjectSync/Temp/Object{o}-{i}", false));
					}
				}
			} 
			mergedController.parameters = mergedController.parameters.Concat(syncParameters).ToArray();
		}

		private void AddBitConversionParameters(int positionBits, List<AnimatorControllerParameter> parameters, int objectCount, int rotationBits)
		{
			for (int p = 0; p < axis.Length; p++)
			{
				for (int b = 0; b < positionBits; b++)
				{
					parameters.Add(GenerateBoolParameter($"CustomObjectSync/Bits/Copy/Position{axis[p]}{b}"));
				}				
			}

			for (int o = 0; o < objectCount; o++)
			{
				parameters.Add(GenerateBoolParameter($"CustomObjectSync/ReadObject/{o}"));
				parameters.Add(GenerateBoolParameter($"CustomObjectSync/StartWrite/{o}"));
				parameters.Add(GenerateBoolParameter($"CustomObjectSync/StartRead/{o}"));
				parameters.Add(GenerateBoolParameter($"CustomObjectSync/ReadInProgress/{o}"));
				parameters.Add(GenerateBoolParameter($"CustomObjectSync/WriteInProgress/{o}"));
				for (int p = 0; p < axis.Length; p++)
				{
					parameters.Add(GenerateFloatParameter($"CustomObjectSync/Temp/Position{axis[p]}/{o}"));
					for (int b = 0; b < positionBits; b++)
					{
						parameters.Add(GenerateBoolParameter($"CustomObjectSync/Bits/Position{axis[p]}{b}/{o}"));
					}				
				}
			}
			
			if (rotationEnabled)
			{
				for (int p = 0; p < axis.Length; p++)
				{
					for (int b = 0; b < rotationBits; b++)
					{
						parameters.Add(GenerateBoolParameter($"CustomObjectSync/Bits/Copy/Rotation{axis[p]}{b}"));
					}				
				}

				for (int o = 0; o < objectCount; o++)
				{
					for (int p = 0; p < axis.Length; p++)
					{
						parameters.Add(GenerateFloatParameter($"CustomObjectSync/Temp/Rotation{axis[p]}/{o}"));
						for (int b = 0; b < rotationBits; b++)
						{
							parameters.Add(GenerateBoolParameter($"CustomObjectSync/Bits/Rotation{axis[p]}{b}/{o}"));
						}				
					}
				}
			}
		}

		private List<AnimatorControllerLayer> GenerateBitConversionLayers(int objectCount, AnimationClip buffer, int positionBits,
			int objectParameterCount, int rotationBits)
		{
			List<AnimatorControllerLayer> bitLayers = new List<AnimatorControllerLayer>();
			for (int o = 0; o < objectCount; o++)
			{
				AnimatorStateMachine positionMachine = GenerateStateMachine($"CustomObjectSync/Position Bit Convert{o}", new Vector3(-80, 0, 0), new Vector3(-80, 200 , 0), new Vector3(-80, 100, 0));
				AnimatorControllerLayer positionLayer = GenerateLayer($"CustomObjectSync/Position Bit Convert{o}", positionMachine);
				ChildAnimatorState initialState = GenerateChildState(new Vector3(-100, 400, 0), GenerateState("Initial", motion: buffer));
			
				SetupAnimationControllerCopy("Position", o, buffer, initialState, positionMachine, positionBits, objectParameterCount, true, positionBits > rotationBits);
				SetupAnimationControllerCopy("Position", o, buffer, initialState, positionMachine, positionBits, objectParameterCount, false, positionBits > rotationBits);
				positionMachine.states = new[] { initialState }.Concat(positionMachine.states).ToArray();
				positionMachine.defaultState = initialState.state;
				bitLayers.Add(positionLayer);
				
				if (rotationEnabled)
				{
					AnimatorStateMachine rotationMachine = GenerateStateMachine($"CustomObjectSync/Rotation Bit Convert{o}", new Vector3(-80, 0, 0), new Vector3(-80, 200 , 0), new Vector3(-80, 100, 0));
					AnimatorControllerLayer rotationLayer = GenerateLayer($"CustomObjectSync/Rotation Bit Convert{o}", rotationMachine);
					ChildAnimatorState initialRotationState = GenerateChildState(new Vector3(-100, 400, 0), GenerateState("Initial", motion: buffer));

					SetupAnimationControllerCopy("Rotation", o, buffer, initialRotationState, rotationMachine, rotationBits, objectParameterCount,  true, positionBits <= rotationBits);	
					SetupAnimationControllerCopy("Rotation", o, buffer, initialRotationState, rotationMachine, rotationBits, objectParameterCount, false, positionBits <= rotationBits);	
					rotationMachine.states = new[] { initialRotationState }.Concat(rotationMachine.states).ToArray();
					rotationMachine.defaultState = initialRotationState.state;
					bitLayers.Add(rotationLayer);
				}	
			}

			return bitLayers;
		}

		private void SetupAnimationControllerCopy(string name, int o, AnimationClip buffer, ChildAnimatorState initialState,
			AnimatorStateMachine machine, int bits, int objectBits, bool read, bool copyBoth)
		{
			bool[][] permutations = GeneratePermutations(3);
			bool[][] objectPermutations = GeneratePermutations(objectBits);
			
			int multiplier = read ? -1 : 1;
			string mode = read ? "Read" : "Write";
			
			#region states
			ChildAnimatorState startState = GenerateChildState(new Vector3((-100) + multiplier * 300, 200, 0), GenerateState($"Start{mode}", motion: buffer));
			ChildAnimatorState endState = GenerateChildState(new Vector3((-100) + multiplier * 300 * (bits + 2) , 200, 0), GenerateState($"End{mode}", motion: buffer));
			
			startState.state.behaviours = new[]
			{
				read 
					? GenerateParameterDriver(axis.Select(x => GenerateParameter(ChangeType.Copy, source: $"CustomObjectSync/{name}{x}", name: $"CustomObjectSync/Temp/{name}{x}/{o}")).ToArray())
					: GenerateParameterDriver(axis.Select(x => GenerateParameter(ChangeType.Set, name: $"CustomObjectSync/Temp/{name}{x}/{o}", value: 0)).ToArray())
			};

			if (copyBoth)
			{
				endState.state.behaviours = new[]
				{
					GenerateParameterDriver(new[]
					{
						GenerateParameter(ChangeType.Set, name: $"CustomObjectSync/Start{mode}/{o}", value: 0) ,
						GenerateParameter(ChangeType.Set, name: $"CustomObjectSync/{mode}InProgress/{o}", value: 0)
					})
				};
				startState.state.behaviours = startState.state.behaviours.Append(GenerateParameterDriver(new [] {GenerateParameter(ChangeType.Set, name: $"CustomObjectSync/{mode}InProgress/{o}", value: 1)})).ToArray();
			}

			
			if (!read && copyBoth)
			{
				endState.state.behaviours = endState.state.behaviours.Append(GenerateParameterDriver(axis.Select(x => GenerateParameter(ChangeType.Copy, source: $"CustomObjectSync/Temp/Position{x}/{o}", name: $"CustomObjectSync/Position{x}")).ToArray())).ToArray();
				endState.state.behaviours = endState.state.behaviours.Append(GenerateParameterDriver(axis.Select(x => GenerateParameter(ChangeType.Copy, source: $"CustomObjectSync/Temp/Rotation{x}/{o}", name: $"CustomObjectSync/Rotation{x}")).ToArray())).ToArray();
				startState.state.behaviours = startState.state.behaviours.Append(GenerateParameterDriver(Enumerable.Range(0 ,objectBits).Select(x => GenerateParameter(ChangeType.Copy, source: $"CustomObjectSync/Sync/Object{x}", name: $"CustomObjectSync/Temp/Object{o}-{x}")).ToArray())).ToArray();
				endState.state.behaviours = endState.state.behaviours.Append(GenerateParameterDriver(Enumerable.Range(0 ,objectBits).Select(x => GenerateParameter(ChangeType.Copy, source: $"CustomObjectSync/Temp/Object{o}-{x}", name: $"CustomObjectSync/Object{x}")).ToArray())).ToArray();
			}

			List<List<ChildAnimatorState>> bitStates = new List<List<ChildAnimatorState>>();
			for (int b = 0; b < bits; b++)
			{
				List<ChildAnimatorState> states = new List<ChildAnimatorState>();
				bitStates.Add(states);
				for (int s = 0; s < 8; s++)
				{
					bool[] perm = permutations[s];
					string permString = perm.Aggregate("", (s1, b1) => s1 += (b1 ? "1" : "0"));
					AnimatorState state = GenerateState($"Bit{mode}-{b}-{permString}", motion: buffer);

					List<Parameter> parameterDriverParams = new List<Parameter>();
					for (int p = 0; p < perm.Length; p++)
					{
						if (read)
						{
							if (perm[p])
							{
								parameterDriverParams.Add(GenerateParameter(ChangeType.Set, name: $"CustomObjectSync/Bits/{name}{axis[p]}{b}/{o}", value: 1));
								parameterDriverParams.Add(GenerateParameter(ChangeType.Add, name: $"CustomObjectSync/Temp/{name}{axis[p]}/{o}", value: -1 * Mathf.Pow(0.5f, b + 1)));
							}
							else
							{
								parameterDriverParams.Add(GenerateParameter(ChangeType.Set, name: $"CustomObjectSync/Bits/{name}{axis[p]}{b}/{o}", value: 0));
							}
						}
						else
						{
							if (perm[p])
							{
								parameterDriverParams.Add(GenerateParameter(ChangeType.Add, name: $"CustomObjectSync/Temp/{name}{axis[p]}/{o}", value: 1 * Mathf.Pow(0.5f, b + 1)));
							}
						}
					}
					state.behaviours = new StateMachineBehaviour[]
					{
						GenerateParameterDriver(parameterDriverParams.ToArray())
					};
					states.Add(GenerateChildState(new Vector3((-100) + multiplier * (b + 2) * 300, s * 50 ), state));
				}
			}
			
			#endregion
			
			#region transitions

			initialState.state.transitions = initialState.state.transitions
				.Append(
					GenerateTransition("", destinationState: startState.state, conditions: 
						Enumerable.Range(0, objectBits).Select(x => GenerateCondition(objectPermutations[o][x] ? AnimatorConditionMode.If : AnimatorConditionMode.IfNot, $"CustomObjectSync/Sync/Object{x}", threshold: 0f))
						.Append(GenerateCondition(AnimatorConditionMode.If, $"CustomObjectSync/Start{mode}/{o}", 0))
						.Append(GenerateCondition( read ? AnimatorConditionMode.If : AnimatorConditionMode.IfNot, "IsLocal", 0)).ToArray())
					).ToArray();
			startState.state.transitions = bitStates[0].Select((x, index) => GenerateBitTransition(permutations[index], 0, o, name, x.state, read)).ToArray();

			for (var i = 0; i < bitStates.Count; i++)
			{
				if (i == bitStates.Count - 1)
				{
					for (var s = 0; s < bitStates[i].Count; s++)
					{
						bitStates[i][s].state.transitions = new[]
						{
							GenerateTransition("", destinationState: endState.state, hasExitTime: true, exitTime: 1)
						};
					}
					continue;
				}

				for (int s1 = 0; s1 < bitStates[i].Count; s1++)
				{
					AnimatorStateTransition[] transitions = new AnimatorStateTransition[bitStates[i + 1].Count];
					for (int s2 = 0; s2 < bitStates[i+1].Count; s2++)
					{
						transitions[s2] = GenerateBitTransition(permutations[s2], i + 1, o, name, bitStates[i + 1][s2].state, read);
					}
					bitStates[i][s1].state.transitions = transitions;
				}
			}

			endState.state.transitions = new[] { GenerateTransition("", destinationState: initialState.state, conditions: new []{GenerateCondition(AnimatorConditionMode.IfNot, $"CustomObjectSync/{mode}InProgress/{o}", 0f)}) };
			
			#endregion
			
			machine.states = machine.states.Concat(new []{ startState, endState }).Concat(bitStates.SelectMany(x => x)).ToArray();
		}

		public void Remove()
		{
			VRCAvatarDescriptor descriptor = syncObjects.Select(x => x.GetComponentInParent<VRCAvatarDescriptor>()).FirstOrDefault();
			
			if (descriptor == null) return;

			Stack<VRCExpressionsMenu> menus = new Stack<VRCExpressionsMenu>(new [] { descriptor.expressionsMenu });
			while (menus.Count > 0)
			{
				VRCExpressionsMenu menu = menus.Pop();
				if (menu == null || menu.controls == null) continue;
				if (menu.controls.Any(x =>
					    x.type == VRCExpressionsMenu.Control.ControlType.Toggle &&
					    x.parameter.name == "CustomObjectSync/Enabled"))
				{
					menu.controls = menu.controls.Where(x => x.parameter.name != "CustomObjectSync/Enabled" && x.parameter.name != "CustomObjectSync/LocalDebugView").ToList();
					EditorUtility.SetDirty(menu);
				}
				menu.controls
					.Where(x => x.type == VRCExpressionsMenu.Control.ControlType.SubMenu && x.subMenu != null)
					.ToList().ForEach(x => menus.Push(x.subMenu));
			}

			if (descriptor.expressionParameters != null && descriptor.expressionParameters.parameters != null &&
			    descriptor.expressionParameters.parameters.Any(x => x.name.Contains("CustomObjectSync")))
			{
				descriptor.expressionParameters.parameters = descriptor.expressionParameters.parameters
					.Where(x => !x.name.Contains("CustomObjectSync/")).ToArray();
				EditorUtility.SetDirty(descriptor.expressionParameters);
			}

			if (descriptor != null &&
			    descriptor.baseAnimationLayers.FirstOrDefault(x => x.type == VRCAvatarDescriptor.AnimLayerType.FX)
				    .animatorController != null &&
			    ((AnimatorController)descriptor.baseAnimationLayers
				    .FirstOrDefault(x => x.type == VRCAvatarDescriptor.AnimLayerType.FX).animatorController).layers
			    .Any(x => x.name.Contains("CustomObjectSync")))
			{
				AnimatorController controller = ((AnimatorController)descriptor.baseAnimationLayers
					.FirstOrDefault(x => x.type == VRCAvatarDescriptor.AnimLayerType.FX).animatorController);
				AnimatorControllerLayer[] layersToDelete = controller.layers.Where(x => x.name.StartsWith("CustomObjectSync/")).ToArray();
				List<Object> assets = new List<Object>();
				
				void AddToAssetsListStateMachineRecursive(List<UnityEngine.Object> usedAssets, AnimatorStateMachine sm) {
					usedAssets.Add(sm);
					foreach (var behaviour in sm.behaviours) usedAssets.Add(behaviour);
					foreach (var transition in sm.anyStateTransitions) usedAssets.Add(transition);
					foreach (var transition in sm.entryTransitions) usedAssets.Add(transition);
					foreach (var state in sm.states) {
						usedAssets.Add(state.state);
						foreach (var behaviour in state.state.behaviours) usedAssets.Add(behaviour);
						foreach (var transition in state.state.transitions) usedAssets.Add(transition);
						if (state.state.motion is BlendTree) {
							usedAssets.Add(state.state.motion);
							var bt = state.state.motion as BlendTree;
							var stack = new Stack<BlendTree>();
							stack.Push(bt);
							while (stack.Count > 0) {
								var current = stack.Pop();
								foreach (var child in current.children) {
									if (child.motion is BlendTree)
									{
										usedAssets.Add(child.motion);
										stack.Push(child.motion as BlendTree);
									}
								}
							}
						}
					}
					foreach (var state_machine in sm.stateMachines) AddToAssetsListStateMachineRecursive(assets, state_machine.stateMachine);
				}
				
				foreach (var animatorControllerLayer in layersToDelete)
				{
					AddToAssetsListStateMachineRecursive(assets, animatorControllerLayer.stateMachine);
				}
				foreach (Object usedAsset in assets)
				{
					AssetDatabase.RemoveObjectFromAsset(usedAsset);
				}

				controller.layers = controller.layers.Where(x => !x.name.StartsWith("CustomObjectSync/")).ToArray();
				controller.parameters = controller.parameters.Where(x => !x.name.Contains("CustomObjectSync/")).ToArray();

				if (controller.layers.Count() < 1)
				{
					AnimatorControllerLayer baseLayer = new AnimatorControllerLayer
					{
						name = "Base Layer",
						defaultWeight = 1f,
						stateMachine = new AnimatorStateMachine()
					};
					AssetDatabase.AddObjectToAsset(baseLayer.stateMachine, controller);
						
					controller.AddLayer(baseLayer);
				}
			}
			
			if (descriptor.transform.Find("Custom Object Sync"))
			{
				void CantFindTarget(Transform userObject)
				{
					// Can't figure out the target, so can't move back to the old path. Just move the object to descriptor.
					string oldPath = AnimationUtility.CalculateTransformPath(userObject.transform, descriptor.transform);
					userObject.parent = descriptor.transform;
					string newPath = AnimationUtility.CalculateTransformPath(userObject.transform, descriptor.transform);
		
					AnimationClip[] allClips = descriptor.baseAnimationLayers.Concat(descriptor.specialAnimationLayers)
						.Where(x => x.animatorController != null).SelectMany(x => x.animatorController.animationClips)
						.ToArray();
					RenameClipPaths(allClips, false, oldPath, newPath);
						
						
					if (userObject.GetComponent<VRCParentConstraint>() != null)
					{
						DestroyImmediate(userObject.GetComponent<VRCParentConstraint>());
					}
				}
				
				Transform prefab = descriptor.transform.Find("Custom Object Sync");
				Transform[] userObjects = Enumerable.Range(0, prefab.childCount)
					.Select(x => prefab.GetChild(x)).Where(x => x.name != "Set" && x.name != "Measure" && x.name != "Target" && !x.name.Contains(" Damping Sync")).ToArray();
				foreach (Transform userObject in userObjects)
				{
					if(userObject == null) continue;
					VRCParentConstraint targetConstraint = userObject.GetComponent<VRCParentConstraint>();
					if (targetConstraint == null)
					{
						CantFindTarget(userObject);
						continue;
					}
					Transform dampingObj = targetConstraint.Sources[0].SourceTransform;
					if (dampingObj == null)
					{
						CantFindTarget(userObject);
						continue;
					};
					
					VRCParentConstraint VRCParentConstraint = dampingObj.gameObject.GetComponent<VRCParentConstraint>();
					if (VRCParentConstraint != null && VRCParentConstraint.Sources.Count == 2 && VRCParentConstraint.Sources[1].SourceTransform != null &&
					    VRCParentConstraint.Sources[1].SourceTransform == prefab.transform.Find("Set/Result"))
					{
						targetConstraint = VRCParentConstraint;
					}

					Transform target = Enumerable.Range(0, targetConstraint.Sources.Count)
							.Select(x => targetConstraint.Sources[x]).Where(x => x.SourceTransform != null && x.SourceTransform.name.EndsWith("Target"))
							.Select(x => x.SourceTransform).FirstOrDefault();
					
					if (target == null)
					{
						CantFindTarget(userObject);
						continue;
					}
					
					string oldPath = GetDescriptorPath(userObject);
					if (GetDescriptorPath(target).StartsWith(AnimationUtility.CalculateTransformPath(prefab, descriptor.transform)))
					{
						target.parent = prefab.parent;// Make sure if the user put the target under the prefab, it doesnt get deleted.
					}
					userObject.parent = target.parent.transform;
					string newPath = GetDescriptorPath(userObject);
		
					AnimationClip[] allClips = descriptor.baseAnimationLayers.Concat(descriptor.specialAnimationLayers)
						.Where(x => x.animatorController != null).SelectMany(x => x.animatorController.animationClips)
						.ToArray();
					RenameClipPaths(allClips, false, oldPath, newPath);
						
					if (userObject.GetComponent<VRCParentConstraint>() != null)
					{
						DestroyImmediate(userObject.GetComponent<VRCParentConstraint>());
					}
					if (userObject.GetComponent<VRCScaleConstraint>() != null)
					{
						DestroyImmediate(userObject.GetComponent<VRCScaleConstraint>());
					}
					
					// Unity Constraints
					var components = target.GetComponents<IConstraint>();
					string targetPath = GetDescriptorPath(target);
					for (var j = 0; j < components.Length; j++)
					{
						// Move constraint animations to the new path.
						AnimationClip[] targetClips = allClips.Where(x =>
							AnimationUtility.GetCurveBindings(x)
								.Any(y => y.type == components[j].GetType() && y.path == targetPath)).ToArray();
						foreach (AnimationClip animationClip in targetClips)
						{
							EditorCurveBinding[] curveBindings = AnimationUtility.GetCurveBindings(animationClip);
							for (var bi = 0; bi < curveBindings.Length; bi++)
							{
								var curveBinding = curveBindings[bi];
								AnimationCurve curve = AnimationUtility.GetEditorCurve(animationClip, curveBinding);
								AnimationUtility.SetEditorCurve(animationClip, curveBinding, null);
								if (curveBinding.type == components[j].GetType() && curveBinding.path == targetPath)
								{
									curveBinding.path = newPath;
								}
								AnimationUtility.SetEditorCurve(animationClip, curveBinding, curve);
							}
						}
						MoveConstraint(components[j], userObject.gameObject);
					}
					
					// VRChat Constraints
					var vrcComponents = target.GetComponents<VRCConstraintBase>();
					string vrcTargetPath = GetDescriptorPath(target);
					for (var j = 0; j < vrcComponents.Length; j++)
					{
						// Move constraint animations to the new path.
						AnimationClip[] targetClips = allClips.Where(x =>
							AnimationUtility.GetCurveBindings(x)
								.Any(y => y.type == vrcComponents[j].GetType() && y.path == vrcTargetPath)).ToArray();
						foreach (AnimationClip animationClip in targetClips)
						{
							EditorCurveBinding[] curveBindings = AnimationUtility.GetCurveBindings(animationClip);
							for (var bi = 0; bi < curveBindings.Length; bi++)
							{
								var curveBinding = curveBindings[bi];
								AnimationCurve curve = AnimationUtility.GetEditorCurve(animationClip, curveBinding);
								AnimationUtility.SetEditorCurve(animationClip, curveBinding, null);
								if (curveBinding.type == vrcComponents[j].GetType() && curveBinding.path == vrcTargetPath)
								{
									curveBinding.path = newPath;
								}
								AnimationUtility.SetEditorCurve(animationClip, curveBinding, curve);
							}
						}
						MoveVRChatConstraint(vrcComponents[j], userObject.gameObject);
					}
						
					DestroyImmediate(target.gameObject);

				}
				DestroyImmediate(prefab.gameObject);
			}
			
			EditorUtility.DisplayDialog("Success!", "Custom Object Sync has been successfully removed", "Ok");
		}

		public string GetDescriptorPath(Transform obj)
		{
			VRCAvatarDescriptor descriptor = obj.GetComponentInParent<VRCAvatarDescriptor>();
			return AnimationUtility.CalculateTransformPath(obj.transform, descriptor.transform);
		}
		
		public string GetDescriptorPath(GameObject obj) => GetDescriptorPath(obj.transform);
		
		public int GetMaxBitCount()
		{
			int rotationBits = rotationEnabled ? (rotationPrecision * 3) : 0;
			int positionBits = 3 * (maxRadius + positionPrecision);
			int maxbitCount = rotationBits + positionBits;
			return maxbitCount;
		}

		public int GetStepCount(int bits = -1)
		{
			bits = bits == -1 ? bitCount : bits;
			return  Mathf.CeilToInt(GetMaxBitCount() / (float)bits);
		}

		public AnimatorStateTransition GenerateBitTransition(bool[] values, int index, int objectIndex, string parameterName, AnimatorState destinationState, bool read)
		{
			AnimatorStateTransition transition = null;
			if (read)
			{
				transition = 
					GenerateTransition("", destinationState: destinationState, conditions: Enumerable.Range(0, values.Length).Select(i =>
						GenerateCondition(
							values[i] ? AnimatorConditionMode.Greater : AnimatorConditionMode.Less, 
							$"CustomObjectSync/Temp/{parameterName}{axis[i]}/{objectIndex}" , 
							Mathf.Pow(0.5f, index + 1) * (values[i] ? 0.9999f : 1.0001f))
					).ToArray());
			}
			else
			{
				transition = 
					GenerateTransition("", destinationState: destinationState, conditions: Enumerable.Range(0, values.Length).Select(i =>
						GenerateCondition(
							values[i] ? AnimatorConditionMode.If : AnimatorConditionMode.IfNot, 
							$"CustomObjectSync/Bits/{parameterName}{axis[i]}{index}/{objectIndex}" , 
							0)
						).ToArray());
			}
			
			

			return transition;
		}
		
		
		bool[][] GeneratePermutations(int size)
		{
			return Enumerable.Range(0, (int)Math.Pow(2, size)).Select(i => Enumerable.Range(0, size).Select(b => ((i & (1 << b)) > 0)).ToArray()).ToArray();
		}

		public bool ObjectPredicate(Func<GameObject, bool> predicate, bool any = true, bool ignoreNulls = true)
		{
			return any ? syncObjects.Where(x => !ignoreNulls || x != null).Any(predicate) : syncObjects.Where(x => !ignoreNulls || x != null).All(predicate);
		}
		
        public static void RenameClipPaths(AnimationClip[] clips, bool replaceEntire, string oldPath, string newPath)
        {
            try
            {
                AssetDatabase.StartAssetEditing();

                foreach (AnimationClip clip in clips)
                {
                    EditorCurveBinding[] floatCurves = AnimationUtility.GetCurveBindings(clip);
                    EditorCurveBinding[] objectCurves = AnimationUtility.GetObjectReferenceCurveBindings(clip);

                    foreach (EditorCurveBinding binding in floatCurves) ChangeBindings(binding, false);
                    foreach (EditorCurveBinding binding in objectCurves) ChangeBindings(binding, true);

                    void ChangeBindings(EditorCurveBinding binding, bool isObjectCurve)
                    {
                        if (isObjectCurve)
                        {
                            ObjectReferenceKeyframe[] objectCurve = AnimationUtility.GetObjectReferenceCurve(clip, binding);

                            if (!replaceEntire && binding.path.StartsWith(oldPath))
                            {
                                AnimationUtility.SetObjectReferenceCurve(clip, binding, null);
                                binding.path = binding.path.Replace(oldPath, newPath);
                                AnimationUtility.SetObjectReferenceCurve(clip, binding, objectCurve);
                            }

                            if (replaceEntire && binding.path == oldPath)
                            {
                                AnimationUtility.SetObjectReferenceCurve(clip, binding, null);
                                binding.path = newPath;
                                AnimationUtility.SetObjectReferenceCurve(clip, binding, objectCurve);
                            }
                        }
                        else
                        {
                            AnimationCurve floatCurve = AnimationUtility.GetEditorCurve(clip, binding);

                            if (!replaceEntire && binding.path.StartsWith(oldPath))
                            {
                                AnimationUtility.SetEditorCurve(clip, binding, null);
                                binding.path = binding.path.Replace(oldPath, newPath);
                                AnimationUtility.SetEditorCurve(clip, binding, floatCurve);
                            }

                            if (replaceEntire && binding.path == oldPath)
                            {
                                AnimationUtility.SetEditorCurve(clip, binding, null);
                                binding.path = newPath;
                                AnimationUtility.SetEditorCurve(clip, binding, floatCurve);
                            }
                        }
                    }
                }
            }
            finally { AssetDatabase.StopAssetEditing();  }
        }
        
        public VRCExpressionsMenu GetMenuFromLocation(VRCAvatarDescriptor descriptor, string location)
        {
	        VRCExpressionsMenu menu = descriptor.expressionsMenu;
	        if (location.StartsWith("/"))
	        {
		        location = location.Substring(1);
	        }
	        if (location.EndsWith("/"))
	        {
		        location = location.Substring(0, location.Length - 1);
	        }
			
	        string[] menus = location.Split('/');
	        
		    if (menus.Length == 1 && menus[0] == "") return menu;
			
	        for (int i = 0; i < menus.Length; i++)
	        {
		        string nextMenu = menus[i];
		        if (menu.controls == null) return null; // Menu's fucked up

		        VRCExpressionsMenu.Control nextMenuControl = menu.controls.Where(x => x.type == VRCExpressionsMenu.Control.ControlType.SubMenu).FirstOrDefault(x => x.name == nextMenu);
		        if (nextMenuControl == null || nextMenuControl.subMenu == null) return null; // Menu not found
				
		        menu = nextMenuControl.subMenu;
	        }
			
	        if (menu.controls == null) return null; // Menu's fucked up
	        return menu;
        }
		
	}
}