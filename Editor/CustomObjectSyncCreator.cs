using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Cysharp.Threading.Tasks;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.VersionControl;
using UnityEngine;
using UnityEngine.Animations;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;
using VRC.SDKBase;
using static VRC.SDKBase.VRC_AvatarParameterDriver;
using static VRLabs.CustomObjectSyncCreator.ControllerGenerationMethods;
using Object = UnityEngine.Object;
using Random = UnityEngine.Random;

namespace VRLabs.CustomObjectSyncCreator
{
	public class CustomObjectSyncCreator : ScriptableSingleton<CustomObjectSyncCreator>
	{
		public AnimatorController resourceController;
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
		public float dampingConstraintValue = 0.1f;
		public bool quickSync;

		public const string STANDARD_NEW_ANIMATION_FOLDER = "Assets/VRLabs/GeneratedAssets/Animations/";
		public const string STANDARD_NEW_ANIMATOR_FOLDER = "Assets/VRLabs/GeneratedAssets/Animators/";
		public const string STANDARD_NEW_PARAMASSET_FOLDER = "Assets/VRLabs/GeneratedAssets/ExpressionParameters/";
		public const string STANDARD_NEW_MENUASSET_FOLDER = "Assets/VRLabs/GeneratedAssets/ExpressionMenu/";
		
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
			VRCExpressionsMenu menuObject = descriptor.expressionsMenu;
			
			Directory.CreateDirectory(STANDARD_NEW_ANIMATOR_FOLDER);
			string uniqueControllerPath = AssetDatabase.GenerateUniqueAssetPath(STANDARD_NEW_ANIMATOR_FOLDER + "CustomObjectCreator.controller");
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
			
			mergedController.parameters = mergedController.parameters.Concat(parameters.Where(p => mergedController.parameters.All(x => x.name != p.name))).ToArray();

			if (!quickSync)
			{
				List<AnimatorControllerLayer> bitLayers = GenerateBitConversionLayers(objectCount, buffer, positionBits, objectParameterCount, rotationBits);
				mergedController.layers = mergedController.layers.Concat(bitLayers).ToArray();
			}
			#endregion
			
			// Sync Steps
			AnimatorControllerLayer syncLayer = SetupSyncLayer(syncSteps, positionBits, rotationBits, objectCount, objectParameterCount, objectPerms, syncStepParameterCount, syncStepPerms);
			SetupSyncLayerParameters(syncSteps, objectCount, objectParameterCount, syncStepParameterCount, syncStepPerms, mergedController);
			
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

			descriptor.expressionsMenu = menuObject;
			#endregion

			GameObject syncSystem = InstallSystem(descriptor, mergedController, parameterObject);
			
			AnimatorControllerLayer displayLayer = SetupDisplayLayer(descriptor, objectCount, syncSystem, buffer, objectParameterCount, objectPerms);
			mergedController.layers = mergedController.layers.Append(syncLayer).Append(displayLayer).ToArray();

			foreach (AnimatorState state in mergedController.layers.Where(x => x.name.Contains("CustomObjectSync")).SelectMany(x => x.stateMachine.states).Select(x => x.state))
			{
				if (state.motion is null)
				{
					state.motion = buffer;
				}
			}
			
			SerializeController(mergedController);
			
			Directory.CreateDirectory(STANDARD_NEW_ANIMATION_FOLDER);
			foreach (var clip in mergedController.animationClips)
			{
				if (!AssetDatabase.IsMainAsset(clip)){
					if (String.IsNullOrEmpty(clip.name))
					{
						clip.name = "Anim";
					}
					var uniqueFileName = AssetDatabase.GenerateUniqueAssetPath($"{STANDARD_NEW_ANIMATION_FOLDER}{clip.name}.anim");
					AssetDatabase.CreateAsset(clip, uniqueFileName);
				}
			}

			EditorUtility.DisplayDialog("Success!", "Custom Object Sync has been successfully installed", "Ok");
		}

		private AnimatorControllerLayer SetupDisplayLayer(VRCAvatarDescriptor descriptor, int objectCount,
			GameObject syncSystem, AnimationClip buffer, int objectParameterCount, bool[][] objectPerms)
		{
			string[] targetStrings = syncObjects.Select(x => AnimationUtility.CalculateTransformPath(addDampeningConstraint ? x.transform.parent.Find($"{x.name} Damping Sync") : x.transform, descriptor.transform)).ToArray();

			AnimationClip enableMeasure = GenerateClip("localMeasureEnabled");
			AddCurve(enableMeasure, "Custom Object Sync/Measure", typeof(GameObject), "m_IsActive", AnimationCurve.Constant(0, 1/60f, 1));
			
			
			AnimationClip remoteParentConstraintOff = GenerateClip("remoteParentConstraintDisabled");
			AddCurve(remoteParentConstraintOff, "Custom Object Sync/Measure", typeof(GameObject), "m_IsActive", AnimationCurve.Constant(0, 1/60f, 0));
			foreach (string targetString in targetStrings)
			{
				AddCurve(remoteParentConstraintOff, targetString, typeof(ParentConstraint), "m_Sources.Array.data[0].weight", AnimationCurve.Constant(0, 1/60f, 1));
				AddCurve(remoteParentConstraintOff, targetString, typeof(ParentConstraint), "m_Sources.Array.data[1].weight", AnimationCurve.Constant(0, 1/60f, 0));
			}
			
			AnimationClip disableDamping = GenerateClip("localDisableDamping");
			foreach (string targetPath in syncObjects.Select(x => AnimationUtility.CalculateTransformPath(x.transform, descriptor.transform)))
			{
				AddCurve(disableDamping, targetPath, typeof(ParentConstraint), "m_Sources.Array.data[0].weight", AnimationCurve.Constant(0, 1/60f, 1));
				AddCurve(disableDamping, targetPath, typeof(ParentConstraint), "m_Sources.Array.data[1].weight", AnimationCurve.Constant(0, 1/60f, 0));
			}
			
			ChildAnimatorState StateIdleRemote = GenerateChildState(new Vector3(30f, 180f, 0f), GenerateState("Idle/Remote"));
			bool[][] axisPermutations = GeneratePermutations(3);
			List<ChildAnimatorState> displayStates = new List<ChildAnimatorState>();
			AnimationClip[] localConstraintTargetClips = Enumerable.Range(0, objectCount).Select(x =>
			{
				string targetString = AnimationUtility.CalculateTransformPath(syncSystem.transform, descriptor.transform) + "/Target";
				AnimationClip localConstraintOn = GenerateClip($"localParentConstraintEnabled{x}");
				Enumerable.Range(0, objectCount).ToList().ForEach(y=> AddCurve(localConstraintOn, targetString, typeof(ParentConstraint), $"m_Sources.Array.data[{y}].weight", AnimationCurve.Constant(0, 1 / 60f, x == y ? 1 : 0)));
				return localConstraintOn;
			}).ToArray();
				
			for (var p = 0; p < axisPermutations.Length; p++)
			{
				bool[] perm = axisPermutations[p];

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
			
			AnimatorState StateRemoteOff = GenerateState("Remote Off", motion: remoteParentConstraintOff);
			List<ChildAnimatorState> remoteOnStates = new List<ChildAnimatorState>();
			AnimationClip[] constraintsEnabled = Enumerable.Range(0, targetStrings.Length).Select(i =>
			{
				string targetString = targetStrings[i];
				AnimationClip remoteParentConstraintOn = GenerateClip($"remoteParentConstraintEnabled{i}");
				AddCurve(remoteParentConstraintOn, "Custom Object Sync/Measure", typeof(GameObject), "m_IsActive", AnimationCurve.Constant(0, 1/60f, 0));
				AddCurve(remoteParentConstraintOn, targetString, typeof(ParentConstraint), "m_Enabled", AnimationCurve.Constant(0, 1/60f, 1));
				AddCurve(remoteParentConstraintOn, targetString, typeof(ParentConstraint), "m_Sources.Array.data[0].weight", AnimationCurve.Constant(0, 1/60f, 0));
				AddCurve(remoteParentConstraintOn, targetString, typeof(ParentConstraint), "m_Sources.Array.data[1].weight", AnimationCurve.Constant(0, 1/60f, 1));
				return remoteParentConstraintOn;
			}).ToArray();
			AnimationClip[] constraintsDisabled = Enumerable.Range(0, targetStrings.Length).Select(i =>
			{
				string targetString = targetStrings[i];
				AnimationClip remoteParentConstraintOffAnim = GenerateClip($"remoteParentConstraintDisabled{i}");
				AddCurve(remoteParentConstraintOffAnim, targetString, typeof(ParentConstraint), "m_Enabled", AnimationCurve.Constant(0, 1/60f, 0));
				AddCurve(remoteParentConstraintOffAnim, targetString, typeof(ParentConstraint), "m_Sources.Array.data[0].weight", AnimationCurve.Constant(0, 1/60f, 0));
				AddCurve(remoteParentConstraintOffAnim, targetString, typeof(ParentConstraint), "m_Sources.Array.data[1].weight", AnimationCurve.Constant(0, 1/60f, 1));
				return remoteParentConstraintOffAnim;
			}).ToArray();
			
			for (int o = 0; o < objectCount; o++)
			{
				AnimatorState remoteOnState = GenerateState("Remote On", writeDefaultValues: true);
				BlendTree remoteTree = GenerateBlendTree("RemoteTree", BlendTreeType.Direct);
				
				AnimationClip rotationXMin = GenerateClip("RotationXMin");
				AddCurve(rotationXMin, "Custom Object Sync/Set/Result", typeof(RotationConstraint), "m_RotationOffset.x", AnimationCurve.Constant(0, 1/60f, -180));
				AnimationClip rotationXMax = GenerateClip("RotationXMax");
				AddCurve(rotationXMax, "Custom Object Sync/Set/Result", typeof(RotationConstraint), "m_RotationOffset.x", AnimationCurve.Constant(0, 1/60f, 180));
				BlendTree rotationXTree = GenerateBlendTree("RotationX", BlendTreeType.Simple1D,
					blendParameter: "CustomObjectSync/RotationX");
				rotationXTree.children = new ChildMotion[]
				{
					GenerateChildMotion(motion: rotationXMin),
					GenerateChildMotion(motion: rotationXMax)
				};
                
				AnimationClip rotationYMin = GenerateClip("RotationYMin");
				AddCurve(rotationYMin, "Custom Object Sync/Set/Result", typeof(RotationConstraint), "m_RotationOffset.y", AnimationCurve.Constant(0, 1/60f, -180));
				AnimationClip rotationYMax = GenerateClip("RotationYMax");
				AddCurve(rotationYMax, "Custom Object Sync/Set/Result", typeof(RotationConstraint), "m_RotationOffset.y", AnimationCurve.Constant(0, 1/60f, 180));
				BlendTree rotationYTree = GenerateBlendTree("RotationY", BlendTreeType.Simple1D,
					blendParameter: "CustomObjectSync/RotationY");
				rotationYTree.children = new ChildMotion[]
				{
					GenerateChildMotion(motion: rotationYMin),
					GenerateChildMotion(motion: rotationYMax)
				};
				
				AnimationClip rotationZMin = GenerateClip("RotationZMin");
				AddCurve(rotationZMin, "Custom Object Sync/Set/Result", typeof(RotationConstraint), "m_RotationOffset.z", AnimationCurve.Constant(0, 1/60f, -180));
				AnimationClip rotationZMax = GenerateClip("RotationZMax");
				AddCurve(rotationZMax, "Custom Object Sync/Set/Result", typeof(RotationConstraint), "m_RotationOffset.z", AnimationCurve.Constant(0, 1/60f, 180));
				BlendTree rotationZTree = GenerateBlendTree("RotationZ", BlendTreeType.Simple1D,
					blendParameter: "CustomObjectSync/RotationZ");
				rotationZTree.children = new ChildMotion[]
				{
					GenerateChildMotion(motion: rotationZMin),
					GenerateChildMotion(motion: rotationZMax)
				};

				BlendTree translationXTree = null;
				BlendTree translationYTree = null;
				BlendTree translationZTree = null;
				
				if (quickSync)
				{
					AnimationClip translationXMin = GenerateClip("TranslationXMin");
					AddCurve(translationXMin, "Custom Object Sync/Set/Result", typeof(PositionConstraint), "m_TranslationOffset.x", AnimationCurve.Constant(0, 1/60f, -Mathf.Pow(2, maxRadius)));
					AnimationClip translationXZero = GenerateClip("TranslationXZero");
					AddCurve(translationXZero, "Custom Object Sync/Set/Result", typeof(PositionConstraint), "m_TranslationOffset.x", AnimationCurve.Constant(0, 1/60f, 0));
					AnimationClip translationXMax = GenerateClip("TranslationXMax");
					AddCurve(translationXMax, "Custom Object Sync/Set/Result", typeof(PositionConstraint), "m_TranslationOffset.x", AnimationCurve.Constant(0, 1/60f, Mathf.Pow(2, maxRadius)));
					translationXTree = GenerateBlendTree("TranslationX", BlendTreeType.Simple1D, blendParameter: "CustomObjectSync/PositionSignX");
					
					BlendTree translationXMinTree = GenerateBlendTree("TranslationXMinTree", blendType: BlendTreeType.Simple1D, blendParameter: "CustomObjectSync/PositionX");
					BlendTree translationXMaxTree = GenerateBlendTree("TranslationXMaxTree", blendType: BlendTreeType.Simple1D, blendParameter: "CustomObjectSync/PositionX");
					translationXMinTree.children = new[]
					{
						GenerateChildMotion(motion: translationXZero),
						GenerateChildMotion(motion: translationXMin)
					};
					translationXMaxTree.children = new[]
					{
						GenerateChildMotion(motion: translationXZero),
						GenerateChildMotion(motion: translationXMax)
					};
					translationXTree.children = new ChildMotion[]
					{
						GenerateChildMotion(translationXMinTree),
						GenerateChildMotion(translationXMaxTree)
					};

					
					AnimationClip translationYMin = GenerateClip("TranslationYMin");
					AddCurve(translationYMin, "Custom Object Sync/Set/Result", typeof(PositionConstraint), "m_TranslationOffset.y", AnimationCurve.Constant(0, 1/60f, -Mathf.Pow(2, maxRadius)));
					AnimationClip translationYZero = GenerateClip("TranslationYZero");
					AddCurve(translationYZero, "Custom Object Sync/Set/Result", typeof(PositionConstraint), "m_TranslationOffset.y", AnimationCurve.Constant(0, 1/60f, 0));
					AnimationClip translationYMax = GenerateClip("TranslationYMax");
					AddCurve(translationYMax, "Custom Object Sync/Set/Result", typeof(PositionConstraint), "m_TranslationOffset.y", AnimationCurve.Constant(0, 1/60f, Mathf.Pow(2, maxRadius)));
					translationYTree = GenerateBlendTree("TranslationY", BlendTreeType.Simple1D, blendParameter: "CustomObjectSync/PositionSignY");
					
					BlendTree translationYMinTree = GenerateBlendTree("TranslationYMinTree", blendType: BlendTreeType.Simple1D, blendParameter: "CustomObjectSync/PositionY");
					BlendTree translationYMaYTree = GenerateBlendTree("TranslationYMaxTree", blendType: BlendTreeType.Simple1D, blendParameter: "CustomObjectSync/PositionY");
					translationYMinTree.children = new[]
					{
						GenerateChildMotion(motion: translationYZero),
						GenerateChildMotion(motion: translationYMin)
					};
					translationYMaYTree.children = new[]
					{
						GenerateChildMotion(motion: translationYZero),
						GenerateChildMotion(motion: translationYMax)
					};
					translationYTree.children = new[]
					{
						GenerateChildMotion(translationYMinTree),
						GenerateChildMotion(translationYMaYTree)
					};
					
					AnimationClip translationZMin = GenerateClip("TranslationZMin");
					AddCurve(translationZMin, "Custom Object Sync/Set/Result", typeof(PositionConstraint), "m_TranslationOffset.z", AnimationCurve.Constant(0, 1/60f, -Mathf.Pow(2, maxRadius)));
					AnimationClip translationZZero = GenerateClip("TranslationZZero");
					AddCurve(translationZZero, "Custom Object Sync/Set/Result", typeof(PositionConstraint), "m_TranslationOffset.z", AnimationCurve.Constant(0, 1/60f, 0));
					AnimationClip translationZMax = GenerateClip("TranslationZMax");
					AddCurve(translationZMax, "Custom Object Sync/Set/Result", typeof(PositionConstraint), "m_TranslationOffset.z", AnimationCurve.Constant(0, 1/60f, Mathf.Pow(2, maxRadius)));
					translationZTree = GenerateBlendTree("TranslationZ", BlendTreeType.Simple1D, blendParameter: "CustomObjectSync/PositionSignZ");
					
					BlendTree translationZMinTree = GenerateBlendTree("TranslationZMinTree", blendType: BlendTreeType.Simple1D, blendParameter: "CustomObjectSync/PositionZ");
					BlendTree translationZMaxTree = GenerateBlendTree("TranslationZMaxTree", blendType: BlendTreeType.Simple1D, blendParameter: "CustomObjectSync/PositionZ");
					translationZMinTree.children = new[]
					{
						GenerateChildMotion(motion: translationZZero),
						GenerateChildMotion(motion: translationZMin)
					};
					translationZMaxTree.children = new[]
					{
						GenerateChildMotion(motion: translationZZero),
						GenerateChildMotion(motion: translationZMax)
					};
					translationZTree.children = new ChildMotion[]
					{
						GenerateChildMotion(translationZMinTree),
						GenerateChildMotion(translationZMaxTree)
					};
				}
				else
				{
					AnimationClip translationXMin = GenerateClip("TranslationXMin");
					AddCurve(translationXMin, "Custom Object Sync/Set/Result", typeof(PositionConstraint), "m_TranslationOffset.x", AnimationCurve.Constant(0, 1/60f, -Mathf.Pow(2, maxRadius)));
					AnimationClip translationXMax = GenerateClip("TranslationXMax");
					AddCurve(translationXMax, "Custom Object Sync/Set/Result", typeof(PositionConstraint), "m_TranslationOffset.x", AnimationCurve.Constant(0, 1/60f, Mathf.Pow(2, maxRadius)));
					translationXTree = GenerateBlendTree("TranslationX", BlendTreeType.Simple1D,
						blendParameter: "CustomObjectSync/PositionX");
					translationXTree.children = new ChildMotion[]
					{
						GenerateChildMotion(motion: translationXMin),
						GenerateChildMotion(motion: translationXMax)
					};
					
					AnimationClip translationYMin = GenerateClip("TranslationYMin");
					AddCurve(translationYMin, "Custom Object Sync/Set/Result", typeof(PositionConstraint), "m_TranslationOffset.y", AnimationCurve.Constant(0, 1/60f, -Mathf.Pow(2, maxRadius)));
					AnimationClip translationYMax = GenerateClip("TranslationYMax");
					AddCurve(translationYMax, "Custom Object Sync/Set/Result", typeof(PositionConstraint), "m_TranslationOffset.y", AnimationCurve.Constant(0, 1/60f, Mathf.Pow(2, maxRadius)));
					translationYTree = GenerateBlendTree("TranslationY", BlendTreeType.Simple1D,
						blendParameter: "CustomObjectSync/PositionY");
					translationYTree.children = new ChildMotion[]
					{
						GenerateChildMotion(motion: translationYMin),
						GenerateChildMotion(motion: translationYMax)
					};
					
					AnimationClip translationZMin = GenerateClip("TranslationZMin");
					AddCurve(translationZMin, "Custom Object Sync/Set/Result", typeof(PositionConstraint), "m_TranslationOffset.z", AnimationCurve.Constant(0, 1/60f, -Mathf.Pow(2, maxRadius)));
					AnimationClip translationZMax = GenerateClip("TranslationZMax");
					AddCurve(translationZMax, "Custom Object Sync/Set/Result", typeof(PositionConstraint), "m_TranslationOffset.z", AnimationCurve.Constant(0, 1/60f, Mathf.Pow(2, maxRadius)));
					translationZTree = GenerateBlendTree("TranslationZ", BlendTreeType.Simple1D,
						blendParameter: "CustomObjectSync/PositionZ");
					translationZTree.children = new ChildMotion[]
					{
						GenerateChildMotion(motion: translationZMin),
						GenerateChildMotion(motion: translationZMax)
					};
				}
				
				
				
				remoteTree.children = new[]
				{
					GenerateChildMotion(rotationXTree, directBlendParameter: "CustomObjectSync/One"),
					GenerateChildMotion(rotationYTree, directBlendParameter: "CustomObjectSync/One"),
					GenerateChildMotion(rotationZTree, directBlendParameter: "CustomObjectSync/One"),
					GenerateChildMotion(translationXTree, directBlendParameter: "CustomObjectSync/One"),
					GenerateChildMotion(translationYTree, directBlendParameter: "CustomObjectSync/One"),
					GenerateChildMotion(translationZTree, directBlendParameter: "CustomObjectSync/One")
				};
				remoteTree.children = remoteTree.children.Concat(Enumerable.Range(0, objectCount).Select(o2 => GenerateChildMotion(o2 == o ? constraintsEnabled[o2] : constraintsDisabled[o2], directBlendParameter: "CustomObjectSync/One"))).ToArray();
				
				remoteOnState.motion = remoteTree;	
				displayStates.Add(GenerateChildState(new Vector3(260f, -60f * (o + 1), 0f), remoteOnState));
				remoteOnStates.Add(displayStates.Last());
			}

			displayStates.Add(GenerateChildState(new Vector3(260f, 0, 0f), StateRemoteOff));
			
			AnimatorStateMachine displayStateMachine = GenerateStateMachine("CustomObjectSync/Parameter Setup and Display", new Vector3(50f, 20f, 0f), new Vector3(50f, 120f, 0f), new Vector3(800f, 120f, 0f), states: displayStates.ToArray(), defaultState: StateIdleRemote.state);

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
				anyStateTransitions.Add(GenerateTransition("", conditions:
					Enumerable.Range(0, objectParameterCount).Select(x => GenerateCondition(objectPerms[o][x] ? AnimatorConditionMode.If : AnimatorConditionMode.IfNot, $"{objParam}{x}", threshold: 0))
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
									$"CustomObjectSync/Position{axis[x]}{(perm[x] ? "Pos" : "Neg")}", -0.000001f),
								GenerateCondition(AnimatorConditionMode.Less,
									$"CustomObjectSync/Position{axis[x]}{(!perm[x] ? "Pos" : "Neg")}", 0.000001f),
							})
							.Append(GenerateCondition(AnimatorConditionMode.If, "IsLocal", 0f))
							.Append(GenerateCondition(AnimatorConditionMode.If, "CustomObjectSync/SetStage", 0f)).ToArray()
						, destinationState: displayStates[2 * p + 1].state)
				);
			}

			displayStateMachine.anyStateTransitions = anyStateTransitions.ToArray();
			
			AnimatorControllerLayer displayLayer = GenerateLayer("CustomObjectSync/Parameter Setup and Display", displayStateMachine);
			return displayLayer;
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
				PositionConstraint sendConstraint = sender.GetComponent<PositionConstraint>();
				sendConstraint.translationAtRest = new Vector3(0, 0, 0);
				ConstraintSource source0 = sendConstraint.GetSource(0);
				source0.weight = 1 - (3f / (radius));	
				sendConstraint.SetSource(0, source0);
				ConstraintSource source1 = sendConstraint.GetSource(1);
				source1.weight = 3f / (radius);
				sendConstraint.SetSource(1, source1);
			}

			Transform mainTargetObject = syncSystem.transform.Find("Target");
			ParentConstraint mainTargetParentConstraint = mainTargetObject.gameObject.AddComponent<ParentConstraint>();
			mainTargetParentConstraint.locked = true;
			mainTargetParentConstraint.constraintActive = true;
			for (var i = 0; i < syncObjects.Length; i++)
			{
				GameObject targetSyncObject = syncObjects[i];
				Transform targetObject = new GameObject($"{targetSyncObject.name} Target").transform;
				targetObject.parent = targetSyncObject.transform.parent;
				targetObject.localPosition = targetSyncObject.transform.localPosition;

				mainTargetParentConstraint.AddSource(new ConstraintSource()
				{
					sourceTransform = targetObject,
					weight = 0f
				});

				string oldPath = AnimationUtility.CalculateTransformPath(targetSyncObject.transform, descriptor.transform);
				targetSyncObject.transform.parent = syncSystem.transform;

				GameObject damping = null;
				if (addDampeningConstraint)
				{
					damping = new GameObject($"{targetSyncObject.name} Damping Sync");
					damping.transform.parent = syncSystem.transform;
					ParentConstraint targetConstraint = targetSyncObject.AddComponent<ParentConstraint>();
					targetConstraint.locked = true;
					targetConstraint.constraintActive = true;
					targetConstraint.AddSource(new ConstraintSource()
					{
						sourceTransform = damping.transform, weight = dampingConstraintValue
					});
					targetConstraint.AddSource(new ConstraintSource()
					{
						sourceTransform = targetSyncObject.transform, weight = 1f
					});
				}
				
				string newPath = AnimationUtility.CalculateTransformPath(targetSyncObject.transform, descriptor.transform);
			
				AnimationClip[] allClips = descriptor.baseAnimationLayers.Concat(descriptor.specialAnimationLayers)
					.Where(x => x.animatorController != null).SelectMany(x => x.animatorController.animationClips)
					.ToArray();
				RenameClipPaths(allClips, false, oldPath, newPath);

				ParentConstraint containerConstraint = addDampeningConstraint ? damping.AddComponent<ParentConstraint>() : targetSyncObject.AddComponent<ParentConstraint>();
				containerConstraint.AddSource(new ConstraintSource()
				{
					sourceTransform = targetObject, weight = 1
				});
				containerConstraint.AddSource(new ConstraintSource()
				{
					sourceTransform = syncSystem.transform.Find("Set/Result"), weight = 0f
				});
				containerConstraint.locked = true;
				containerConstraint.constraintActive = true;	
			}
			Transform setTransform = syncSystem.transform.Find("Set");
			Transform measureTransform = syncSystem.transform.Find("Measure");
			float offset = -Mathf.Pow(2, maxRadius) / 10f;
			setTransform.localPosition = new Vector3(offset, offset, offset);
			measureTransform.localPosition = new Vector3(offset, offset, offset);
			if (centeredOnAvatar)
			{
				PositionConstraint setConstraint = setTransform.gameObject.AddComponent<PositionConstraint>();
				PositionConstraint measureConstraint = measureTransform.gameObject.AddComponent<PositionConstraint>();
				setConstraint.AddSource(new ConstraintSource()
				{
					sourceTransform = descriptor.transform, weight = 1f
				});
				measureConstraint.AddSource(new ConstraintSource()
				{
					sourceTransform = descriptor.transform, weight = 1f
				});
				setConstraint.translationAtRest = Vector3.zero;
				setConstraint.translationOffset = Vector3.zero;
				setConstraint.locked = true;
				setConstraint.constraintActive = true;
				measureConstraint.translationAtRest = Vector3.zero;
				measureConstraint.translationOffset = Vector3.zero;
				measureConstraint.locked = true;
				measureConstraint.constraintActive = true;
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
			AddCurve(bufferWaitInit, "Custom Object Sync/Measure", typeof(PositionConstraint), "m_Enabled", AnimationCurve.Constant(0, ((Math.Max(positionBits, rotationBits)*1.5f))/60f, 0));
			AddCurve(bufferWaitInit, "Custom Object Sync/Set", typeof(PositionConstraint), "m_Enabled", AnimationCurve.Constant(0, ((Math.Max(positionBits, rotationBits)*1.5f))/60f, 0));

			AnimationClip bufferWaitSync = GenerateClip($"BufferWaitSync");
			AddCurve(bufferWaitSync, "Custom Object Sync/Measure", typeof(PositionConstraint), "m_Enabled", AnimationCurve.Constant(0, 12/60f, 0));
			AddCurve(bufferWaitSync, "Custom Object Sync/Set", typeof(PositionConstraint), "m_Enabled", AnimationCurve.Constant(0, 12/60f, 0));
			
			AnimationClip enableWorldConstraint = GenerateClip($"EnableWorldConstraint");
			AddCurve(enableWorldConstraint, "Custom Object Sync/Measure", typeof(PositionConstraint), "m_Enabled", AnimationCurve.Constant(0, 12/60f, 1));
			AddCurve(enableWorldConstraint, "Custom Object Sync/Set", typeof(PositionConstraint), "m_Enabled", AnimationCurve.Constant(0, 12/60f, 1));
			
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

			if (descriptor.expressionsMenu != null && descriptor.expressionsMenu.controls != null &&
			    descriptor.expressionsMenu.controls.Any(x => x.parameter.name == "CustomObjectSync/Enabled"))
			{
				descriptor.expressionsMenu.controls = descriptor.expressionsMenu.controls
					.Where(x => x.parameter.name != "CustomObjectSync/Enabled").ToList();
			}

			if (descriptor.expressionParameters != null && descriptor.expressionParameters.parameters != null &&
			    descriptor.expressionParameters.parameters.Any(x => x.name.Contains("CustomObjectSync")))
			{
				descriptor.expressionParameters.parameters = descriptor.expressionParameters.parameters
					.Where(x => !x.name.Contains("CustomObjectSync/")).ToArray();
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
				string[] layerNames = new[] {"CustomObjectSync/Parameter Setup and Display", "CustomObjectSync/Position Bit Convert" , "CustomObjectSync/Rotation Bit Convert", "CustomObjectSync/Sync" };
				AnimatorControllerLayer[] layersToDelete = controller.layers.Where(x => x.name.StartsWith("CustomObjectSync/")).ToArray();
				List<Object> usedAssets = new List<Object>();
				
				static void AddToAssetsListStateMachineRecursive(List<UnityEngine.Object> usedAssets, AnimatorStateMachine sm) {
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
					foreach (var state_machine in sm.stateMachines) AddToAssetsListStateMachineRecursive(usedAssets, state_machine.stateMachine);
				}
				
				foreach (var animatorControllerLayer in layersToDelete)
				{
					AddToAssetsListStateMachineRecursive(usedAssets, animatorControllerLayer.stateMachine);
				}
				foreach (Object usedAsset in usedAssets)
				{
					AssetDatabase.RemoveObjectFromAsset(usedAsset);
				}
				
				controller.layers = controller.layers.Where(x => layerNames.All(y => !x.name.StartsWith("CustomObjectSync/"))).ToArray();
				controller.parameters = controller.parameters.Where(x => !x.name.Contains("CustomObjectSync/")).ToArray();
			}
			
			if (descriptor.transform.Find("Custom Object Sync"))
			{
				Transform prefab = descriptor.transform.Find("Custom Object Sync");
				Transform[] userObjects = Enumerable.Range(0, prefab.childCount)
					.Select(x => prefab.GetChild(x)).Where(x => x.name != "Set" && x.name != "Measure" && x.name != "Target"&& !x.name.Contains(" Damping Sync")).ToArray();
				foreach (Transform userObject in userObjects)
				{
					ParentConstraint targetConstraint = userObject.GetComponent<ParentConstraint>();
					if (targetConstraint != null)
					{
						Transform dampingObj = targetConstraint.GetSource(0).sourceTransform;
						ParentConstraint parentConstraint = dampingObj.gameObject.GetComponent<ParentConstraint>();
						if (parentConstraint != null)
						{
							targetConstraint = parentConstraint;
						}

						Transform target = Enumerable.Range(0, targetConstraint.sourceCount)
								.Select(x => targetConstraint.GetSource(x)).Where(x => x.sourceTransform.name.EndsWith("Target"))
								.Select(x => x.sourceTransform).FirstOrDefault();
						if (target != null)
						{
							string oldPath = AnimationUtility.CalculateTransformPath(userObject.transform, descriptor.transform);
							userObject.parent = target.parent.transform;
							string newPath = AnimationUtility.CalculateTransformPath(userObject.transform, descriptor.transform);
			
							AnimationClip[] allClips = descriptor.baseAnimationLayers.Concat(descriptor.specialAnimationLayers)
								.Where(x => x.animatorController != null).SelectMany(x => x.animatorController.animationClips)
								.ToArray();
							RenameClipPaths(allClips, false, oldPath, newPath);
							DestroyImmediate(target.gameObject);
						}

						if (userObject.GetComponent<ParentConstraint>() != null)
						{
							DestroyImmediate(userObject.GetComponent<ParentConstraint>());
						}
					}
				}
				Transform result = prefab.Find("Set/Result");
				DestroyImmediate(prefab.gameObject);
			}
			
			EditorUtility.DisplayDialog("Success!", "Custom Object Sync has been successfully removed", "Ok");
		}

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

                            if (!replaceEntire && binding.path.Contains(oldPath))
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

                            if (!replaceEntire && binding.path.Contains(oldPath))
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
		
	}
}