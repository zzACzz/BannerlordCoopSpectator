using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using CoopSpectator.Infrastructure;
using CoopSpectator.MissionBehaviors;
using CoopSpectator.Patches;
using TaleWorlds.Core;
using TaleWorlds.Engine.GauntletUI;
using TaleWorlds.InputSystem;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.View;
using TaleWorlds.MountAndBlade.View.MissionViews;
using TaleWorlds.ObjectSystem;
using TaleWorlds.ScreenSystem;
using TaleWorlds.TwoDimension;

namespace CoopSpectator.UI
{
    public sealed class CoopHeroCreatorMissionView : MissionView
    {
        private enum CreatorStage
        {
            Waiting,
            Culture,
            Face,
            Stats
        }

        private enum FaceEditorOutcome
        {
            None,
            Accepted,
            Cancelled,
            Terminal
        }

        private GauntletLayer _layer;
        private GauntletMovieIdentifier _movie;
        private CoopHeroCultureSelectionVM _cultureViewModel;
        private CoopHeroCreatorVM _statsViewModel;
        private CoopHeroPreviewCharacter _previewCharacter;
        private CoopHeroCreationServerEnvelope _latestEnvelope;
        private CoopHeroCreationRules _rules;
        private SpriteCategory _characterCreationSpriteCategory;
        private CreatorStage _stage = CreatorStage.Waiting;
        private CreatorStage _stageBeforeFace = CreatorStage.Culture;
        private FaceEditorOutcome _faceOutcome;
        private string _selectedCultureId;
        private string _previewCultureId;
        private bool _faceGeneratorOpen;
        private bool _beginEditingSent;
        private float _startupDelay = 0.25f;

        public override void OnBehaviorInitialize()
        {
            base.OnBehaviorInitialize();
            CoopHeroCreationMissionNetwork.ClientEnvelopeReceived += OnEnvelope;
        }

        public override void OnMissionScreenInitialize()
        {
            base.OnMissionScreenInitialize();
            ViewOrderPriority = 40;
        }

        public override void OnMissionScreenTick(float dt)
        {
            base.OnMissionScreenTick(dt);
            if (!GameNetwork.IsClient) return;

            _cultureViewModel?.Tick();
            _statsViewModel?.Tick();

            if (TryCompleteFaceEditorReturn()) return;

            if (_layer != null)
            {
                if (_layer.Input.IsKeyReleased(InputKey.Escape)) MissionScreen?.OnEscape();
                return;
            }

            if (_faceGeneratorOpen) return;
            _startupDelay -= dt;
            if (_startupDelay > 0f) return;

            if (_latestEnvelope == null)
                _latestEnvelope = CoopHeroCreationMissionNetwork.CurrentClientEnvelope;
            TryStartFlow();
        }

        public override void OnMissionScreenActivate()
        {
            base.OnMissionScreenActivate();
            TryCompleteFaceEditorReturn();
        }

        private bool TryCompleteFaceEditorReturn()
        {
            if (!_faceGeneratorOpen || _faceOutcome == FaceEditorOutcome.None) return false;

            FaceEditorOutcome outcome = _faceOutcome;
            _faceOutcome = FaceEditorOutcome.None;
            _faceGeneratorOpen = false;
            CoopHeroCreatorBodyGeneratorPatch.Disarm();

            if (outcome == FaceEditorOutcome.Accepted && _previewCharacter != null)
            {
                try
                {
                    BodyProperties body = NormalizeAge(_previewCharacter.GetBodyProperties(null));
                    _previewCharacter.UpdatePlayerCharacterBodyProperties(body, _previewCharacter.Race, _previewCharacter.IsFemale);
                    ShowStats(body);
                    return true;
                }
                catch (Exception ex)
                {
                    ModLogger.Error("CoopHeroCreatorMissionView: failed to collect face editor result.", ex);
                }
            }

            if (outcome == FaceEditorOutcome.Cancelled)
            {
                try
                {
                    if (_stageBeforeFace == CreatorStage.Stats && _previewCharacter != null)
                        ShowStats(NormalizeAge(_previewCharacter.GetBodyProperties(null)));
                    else
                        ShowCulture();
                    return true;
                }
                catch (Exception ex)
                {
                    ModLogger.Error("CoopHeroCreatorMissionView: failed to restore the previous creator stage.", ex);
                }
            }

            ShowCulture();
            return true;
        }

        public override void OnMissionScreenFinalize()
        {
            CoopHeroCreationMissionNetwork.ClientEnvelopeReceived -= OnEnvelope;
            CoopHeroCreatorBodyGeneratorPatch.Disarm();
            ReleaseLayer();
            try { _cultureViewModel?.OnFinalize(); } catch { }
            try { _statsViewModel?.OnFinalize(); } catch { }
            _cultureViewModel = null;
            _statsViewModel = null;
            ReleaseCharacterCreationSpriteCategory();
            base.OnMissionScreenFinalize();
        }

        private void TryStartFlow()
        {
            if (MissionScreen == null || _layer != null || _faceGeneratorOpen || _latestEnvelope?.Rules == null) return;
            _rules = _latestEnvelope.Rules;

            if (!_beginEditingSent && IsEditable(_latestEnvelope.State))
            {
                _beginEditingSent = CoopHeroCreationMissionNetwork.SendBeginEditing();
            }

            ShowCulture();
        }

        private void OnEnvelope(CoopHeroCreationServerEnvelope envelope)
        {
            if (envelope == null) return;
            _latestEnvelope = envelope;
            if (_rules == null && envelope.Rules != null) _rules = envelope.Rules;
            _cultureViewModel?.ApplyServerEnvelope(envelope);
            _statsViewModel?.ApplyServerEnvelope(envelope);

            if (_faceGeneratorOpen && CoopHeroCreationContract.IsTerminal(envelope.State))
            {
                _faceOutcome = FaceEditorOutcome.Terminal;
                CoopHeroCreatorBodyGeneratorPatch.Disarm();
                try { ScreenManager.PopScreen(); }
                catch (Exception ex)
                {
                    ModLogger.Error("CoopHeroCreatorMissionView: failed to close expired face editor.", ex);
                }
            }

            if (_stage == CreatorStage.Waiting && !_faceGeneratorOpen) TryStartFlow();
        }

        private void ShowCulture()
        {
            if (MissionScreen == null || _rules == null) return;
            ReleaseLayer();
            EnsureCharacterCreationSpriteCategoryLoaded();

            if (_cultureViewModel == null)
                _cultureViewModel = new CoopHeroCultureSelectionVM(_rules, OpenFaceGeneratorFromCulture);
            _cultureViewModel.RestoreSelection(_selectedCultureId);
            if (_latestEnvelope != null) _cultureViewModel.ApplyServerEnvelope(_latestEnvelope);

            CreateLayer("CoopHeroCultureSelection", _cultureViewModel);
            _stage = CreatorStage.Culture;
        }

        private void ShowStats(BodyProperties body)
        {
            if (MissionScreen == null || _rules == null || _previewCharacter == null) return;
            ReleaseLayer();

            if (_statsViewModel == null)
                _statsViewModel = new CoopHeroCreatorVM(OpenFaceGeneratorFromStats, ShowCulture);
            int age = Math.Max(_rules.MinimumAge, Math.Min(_rules.MaximumAge, (int)Math.Round(body.Age)));
            _statsViewModel.Configure(_rules, _selectedCultureId, body.ToString(), _previewCharacter.IsFemale, age);
            if (_latestEnvelope != null) _statsViewModel.ApplyServerEnvelope(_latestEnvelope);

            CreateLayer("CoopHeroCreator", _statsViewModel);
            _stage = CreatorStage.Stats;
        }

        private void CreateLayer(string movieName, ViewModel viewModel)
        {
            try
            {
                _layer = new GauntletLayer("CoopHeroCreatorLayer", ViewOrderPriority, false) { IsFocusLayer = true };
                MissionScreen.AddLayer(_layer);
                _movie = _layer.LoadMovie(movieName, viewModel);
                CaptureInput();
            }
            catch (Exception ex)
            {
                ModLogger.Error("CoopHeroCreatorMissionView: failed to open " + movieName + ".", ex);
                ReleaseLayer();
            }
        }

        private void ReleaseLayer()
        {
            try
            {
                _layer?.InputRestrictions.ResetInputRestrictions();
                _layer?.InputRestrictions.SetMouseVisibility(false);
                if (_layer != null && _movie != null) _layer.ReleaseMovie(_movie);
                if (_layer != null) MissionScreen?.RemoveLayer(_layer);
            }
            catch (Exception ex)
            {
                ModLogger.Info("CoopHeroCreatorMissionView: layer release failed. Error=" + ex.Message);
            }
            _movie = null;
            _layer = null;
        }

        private void CaptureInput()
        {
            if (_layer == null) return;
            _layer.InputRestrictions.SetInputRestrictions(true, InputUsageMask.All);
            _layer.InputRestrictions.SetMouseVisibility(true);
        }

        private void OpenFaceGeneratorFromCulture(string cultureId)
        {
            _selectedCultureId = cultureId;
            OpenFaceGenerator(CreatorStage.Culture);
        }

        private void OpenFaceGeneratorFromStats()
        {
            OpenFaceGenerator(CreatorStage.Stats);
        }

        private void OpenFaceGenerator(CreatorStage sourceStage)
        {
            if (_rules == null || !IsEditable(_latestEnvelope?.State ?? CoopHeroCreationParticipantState.Late)) return;

            try
            {
                BasicCharacterObject template = ResolvePreviewTemplate(_selectedCultureId);
                if (template == null)
                {
                    ModLogger.Info("CoopHeroCreatorMissionView: face editor unavailable because no preview template was found. Culture=" + (_selectedCultureId ?? "null") + ".");
                    return;
                }

                bool cultureChanged = _previewCharacter == null ||
                                      !string.Equals(_previewCultureId, _selectedCultureId, StringComparison.OrdinalIgnoreCase);
                if (cultureChanged)
                {
                    _previewCharacter = new CoopHeroPreviewCharacter(template);
                    BodyProperties initialBody = WithAge(template.GetBodyProperties(template.Equipment), _rules.MinimumAge);
                    _previewCharacter.UpdatePlayerCharacterBodyProperties(initialBody, template.Race, false);
                    _previewCultureId = _selectedCultureId;
                }

                BodyProperties desiredBody = NormalizeAge(_previewCharacter.GetBodyProperties(null));
                ReleaseLayer();
                _stageBeforeFace = sourceStage;
                _stage = CreatorStage.Face;
                _faceOutcome = FaceEditorOutcome.None;

                var faceScreen = ViewCreator.CreateMBFaceGeneratorScreen(_previewCharacter, true, null);
                ConfigureFaceEditorAge(faceScreen, desiredBody.Age);
                CoopHeroCreatorBodyGeneratorPatch.Arm(OnFaceEditorDone, OnFaceEditorCancelled);
                _faceGeneratorOpen = true;
                ScreenManager.PushScreen(faceScreen);
            }
            catch (Exception ex)
            {
                _faceGeneratorOpen = false;
                CoopHeroCreatorBodyGeneratorPatch.Disarm();
                ModLogger.Error("CoopHeroCreatorMissionView: face editor open failed.", ex);
                if (sourceStage == CreatorStage.Stats && _previewCharacter != null)
                    ShowStats(NormalizeAge(_previewCharacter.GetBodyProperties(null)));
                else
                    ShowCulture();
            }
        }

        private void OnFaceEditorDone()
        {
            _faceOutcome = FaceEditorOutcome.Accepted;
        }

        private void OnFaceEditorCancelled()
        {
            _faceOutcome = FaceEditorOutcome.Cancelled;
        }

        private void ConfigureFaceEditorAge(object faceScreen, float desiredAge)
        {
            try
            {
                object handler = faceScreen?.GetType().GetProperty("Handler", BindingFlags.Instance | BindingFlags.Public)
                    ?.GetValue(faceScreen, null);
                object dataSource = handler?.GetType().GetProperty("DataSource", BindingFlags.Instance | BindingFlags.Public)
                    ?.GetValue(handler, null);
                object bodyProperties = dataSource?.GetType().GetProperty("BodyProperties", BindingFlags.Instance | BindingFlags.Public)
                    ?.GetValue(dataSource, null);
                if (!(bodyProperties is IEnumerable properties))
                    throw new MissingMemberException("FaceGenVM.BodyProperties");

                foreach (object property in properties)
                {
                    FieldInfo keyField = property?.GetType().GetField("KeyNo", BindingFlags.Instance | BindingFlags.Public);
                    if (keyField == null || Convert.ToInt32(keyField.GetValue(property)) != -11) continue;
                    SetReflectedProperty(property, "Min", (float)_rules.MinimumAge);
                    SetReflectedProperty(property, "Max", (float)_rules.MaximumAge);
                    SetReflectedProperty(property, "IsDiscrete", true);
                    SetReflectedProperty(property, "Value", (float)Math.Round(Math.Max(_rules.MinimumAge, Math.Min(_rules.MaximumAge, desiredAge))));
                    return;
                }

                throw new MissingMemberException("FaceGen age slider");
            }
            catch (Exception ex)
            {
                ModLogger.Error("CoopHeroCreatorMissionView: failed to constrain native face-editor age.", ex);
                throw;
            }
        }

        private static void SetReflectedProperty(object instance, string propertyName, object value)
        {
            PropertyInfo property = instance.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);
            if (property == null || !property.CanWrite) throw new MissingMemberException(instance.GetType().FullName, propertyName);
            property.SetValue(instance, value, null);
        }

        private BodyProperties NormalizeAge(BodyProperties body)
        {
            int age = Math.Max(_rules.MinimumAge, Math.Min(_rules.MaximumAge, (int)Math.Round(body.Age)));
            return WithAge(body, age);
        }

        private static BodyProperties WithAge(BodyProperties body, int age)
        {
            return new BodyProperties(new DynamicBodyProperties(age, body.Weight, body.Build), body.StaticProperties);
        }

        private void EnsureCharacterCreationSpriteCategoryLoaded()
        {
            if (_characterCreationSpriteCategory != null) return;
            try { _characterCreationSpriteCategory = UIResourceManager.LoadSpriteCategory("ui_charactercreation"); }
            catch (Exception ex)
            {
                ModLogger.Info("CoopHeroCreatorMissionView: character-creation sprite category load failed. Error=" + ex.Message);
            }
        }

        private void ReleaseCharacterCreationSpriteCategory()
        {
            if (_characterCreationSpriteCategory == null) return;
            try { _characterCreationSpriteCategory.Unload(); }
            catch (Exception ex)
            {
                ModLogger.Info("CoopHeroCreatorMissionView: character-creation sprite category unload failed. Error=" + ex.Message);
            }
            _characterCreationSpriteCategory = null;
        }

        private static bool IsEditable(CoopHeroCreationParticipantState state)
        {
            return state == CoopHeroCreationParticipantState.Invited || state == CoopHeroCreationParticipantState.Editing;
        }

        private static BasicCharacterObject ResolvePreviewTemplate(string cultureId)
        {
            MBObjectManager manager = MBObjectManager.Instance;
            if (manager == null) return null;

            BasicCharacterObject classFallback = null;
            try
            {
                foreach (MultiplayerClassDivisions.MPHeroClass heroClass in MultiplayerClassDivisions.GetMPHeroClasses())
                {
                    if (heroClass == null) continue;
                    BasicCharacterObject[] candidates = { heroClass.HeroCharacter, heroClass.TroopCharacter };
                    foreach (BasicCharacterObject candidate in candidates)
                    {
                        if (!IsPreviewTemplateCandidate(candidate)) continue;
                        if (classFallback == null) classFallback = candidate;
                        if (string.Equals(candidate.Culture?.StringId, cultureId, StringComparison.OrdinalIgnoreCase))
                            return candidate;
                    }
                }
            }
            catch (Exception ex)
            {
                ModLogger.Info("CoopHeroCreatorMissionView: MP class template lookup failed. Error=" + ex.Message);
            }

            string normalizedCulture = string.IsNullOrWhiteSpace(cultureId) ? "empire" : cultureId.Trim().ToLowerInvariant();
            string[] preferredIds =
            {
                "mp_coop_light_infantry_" + normalizedCulture + "_hero",
                "mp_coop_heavy_infantry_" + normalizedCulture + "_hero",
                "mp_coop_light_infantry_empire_hero",
                "mp_coop_heavy_infantry_empire_hero",
                "mp_coop_heavy_infantry_vlandia_hero"
            };
            foreach (string id in preferredIds)
            {
                BasicCharacterObject character = manager.GetObject<BasicCharacterObject>(id);
                if (IsPreviewTemplateCandidate(character)) return character;
            }

            if (classFallback != null) return classFallback;

            try
            {
                MethodInfo getObjectTypeList = typeof(MBObjectManager).GetMethods(BindingFlags.Public | BindingFlags.Instance)
                    .FirstOrDefault(method => method.Name == "GetObjectTypeList" && method.IsGenericMethodDefinition && method.GetParameters().Length == 0);
                object objectList = getObjectTypeList?.MakeGenericMethod(typeof(BasicCharacterObject)).Invoke(manager, null);
                if (objectList is IEnumerable enumerable)
                {
                    BasicCharacterObject objectFallback = null;
                    foreach (object item in enumerable)
                    {
                        BasicCharacterObject candidate = item as BasicCharacterObject;
                        if (!IsPreviewTemplateCandidate(candidate)) continue;
                        if (!candidate.StringId.StartsWith("mp_", StringComparison.OrdinalIgnoreCase) &&
                            !candidate.StringId.StartsWith("multiplayer_", StringComparison.OrdinalIgnoreCase)) continue;
                        if (objectFallback == null) objectFallback = candidate;
                        if (string.Equals(candidate.Culture?.StringId, cultureId, StringComparison.OrdinalIgnoreCase)) return candidate;
                    }
                    return objectFallback;
                }
            }
            catch (Exception ex)
            {
                ModLogger.Info("CoopHeroCreatorMissionView: MP object template lookup failed. Error=" + ex.Message);
            }

            return null;
        }

        private static bool IsPreviewTemplateCandidate(BasicCharacterObject character)
        {
            if (character == null || character.IsMounted || string.IsNullOrWhiteSpace(character.StringId)) return false;
            return !character.StringId.StartsWith("dummy_", StringComparison.OrdinalIgnoreCase) &&
                   character.StringId.IndexOf("template", StringComparison.OrdinalIgnoreCase) < 0;
        }
    }

    internal sealed class CoopHeroPreviewCharacter : BasicCharacterObject
    {
        public CoopHeroPreviewCharacter(BasicCharacterObject template)
        {
            FillFrom(template);
        }
    }
}
