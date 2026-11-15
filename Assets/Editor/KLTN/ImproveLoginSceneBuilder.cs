#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using KLTN.Audio;
using KLTN.UI.Authentication;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace KLTN.Game.Editor
{
    /// <summary>Builds an isolated, editable scene. Never saves over LoginScene or shared prefabs.</summary>
    public static class ImproveLoginSceneBuilder
    {
        public const string ScenePath = "Assets/Scenes/ImproveLoginScene.unity";
        private const string Root = "Assets/Ui/ImproveLogin";
        private static readonly Color Cream = Hex("E9DDCE");
        private static readonly Color Red = Hex("A62C32");
        private static readonly Color Pastel = Hex("D96862");
        private static readonly Color Accent = Hex("E57B73");
        private static TMP_FontAsset regular, semibold;
        private static Sprite fill, outline;
        private static Material alphaTint;

        [MenuItem("Tools/KLTN/Build ImproveLoginScene")]
        public static void Build()
        {
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            if (File.Exists(ScenePath))
                throw new InvalidOperationException("ImproveLoginScene already exists. This builder will not overwrite your scene edits.");
            PrepareAssets();
            if (!AssetDatabase.CopyAsset("Assets/Scenes/LoginScene.unity", ScenePath))
                throw new InvalidOperationException("Unable to clone LoginScene.");
            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            Canvas canvas = Find<Canvas>("Canvas");
            var scaler = canvas.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;

            var backgroundSprite = Find<Image>("Background").sprite;
            var gameSprite = Find<Image>("NameGame").sprite;
            var calligraphySprite = Find<Image>("Stick").sprite;
            var universitySprite = Find<Image>("NameUni").sprite;
            var manager = Find<AuthUIManager>("UIManager");
            var feedback = Find<AuthFeedbackView>("NotificationContainer");
            Object.DestroyImmediate(Find<RectTransform>("FormsRoot").gameObject);

            // Fixed design surface fits inside the responsive Canvas without distorting the art.
            var design = Rect("ImprovedLoginDesign", canvas.transform, 0, 0, 1920, 1080);
            design.SetAsFirstSibling();
            var bg = Picture("Background", design, backgroundSprite, 0, 0, 1920, 1080, Color.white);
            bg.preserveAspect = false;
            Picture("Atmosphere", design, null, 0, 0, 1920, 1080, new Color(.055f,.052f,.075f,.25f));
            // A soft horizontal shade removes distracting clouds behind the form and logos.
            var shade = Picture("LeftShade", design, LoadSprite("left-shade"), -520, 0, 1040, 1080, Color.white);
            shade.type = Image.Type.Simple;
            shade.preserveAspect = false;

            var brand = Rect("Brand", design, -530, 450, 710, 100);
            Picture("GameLogo", brand, gameSprite, -225, 0, 260, 82, Red, true);
            Picture("BrandDivider1", brand, null, -76, 0, 2, 66, Cream);
            Picture("Calligraphy", brand, calligraphySprite, -16, 0, 65, 100, Cream, true);
            Picture("BrandDivider2", brand, null, 45, 0, 2, 66, Cream);
            // Separate sprite rectangles preserve the source logo geometry; no shared asset changes.
            var texture = universitySprite.texture;
            float split = texture.width * .235f;
            var emblem = SaveSprite("UniversityEmblem", texture, new Rect(0,0,split,texture.height));
            var wordmark = SaveSprite("UniversityWordmark", texture, new Rect(split,0,texture.width-split,texture.height));
            Picture("UniversityEmblem", brand, emblem, 104, 0, 59, 72, Red, true);
            Picture("UniversityWordmark", brand, wordmark, 257, 0, 220, 72, Cream, true);

            var forms = Rect("FormsRoot", design, 0, 0, 1920, 1080);
            var group = forms.gameObject.AddComponent<CanvasGroup>();
            var frame = Rect("AuthFrame", forms, -560, -60, 660, 850);
            Picture("PanelFill", frame, fill, 0, 0, 660, 850, new Color(.12f,.09f,.105f,.94f));
            Picture("PanelBorder", frame, outline, 0, 0, 660, 850, Red);
            Corners(frame);
            Picture("Lotus", frame, LoadSprite("lotus"), 0, 377, 96, 62, Accent, true);

            var loginForm = Rect("LoginForm", frame, 0, 0, 660, 850);
            Header(loginForm, "Chào mừng trở lại", "Đăng nhập");
            TMP_InputField email = Input(loginForm, "LoginEmail", "Email", 150, false);
            TMP_InputField password = Input(loginForm, "LoginPassword", "Mật khẩu", 48, true);
            var login = loginForm.gameObject.AddComponent<LoginUI>();
            Bind(login, "emailInput", email, "passwordInput", password, "authUIManager", manager, "feedbackView", feedback);
            var loginMethods = Rect("LoginMethods", frame, 0, 0, 660, 850);
            var google = loginMethods.gameObject.AddComponent<GoogleLoginUI>();
            Bind(google, "authUIManager", manager, "feedbackView", feedback);
            var registerForm = Rect("RegisterForm", frame, 0, 0, 660, 850);
            Header(registerForm, "Bắt đầu hành trình", "Tạo tài khoản");
            var registerEmail = Input(registerForm, "RegisterEmail", "Email", 150, false);
            var registerPassword = Input(registerForm, "RegisterPassword", "Mật khẩu", 48, true);
            var confirm = Input(registerForm, "ConfirmPassword", "Xác nhận mật khẩu", -54, true);
            var register = registerForm.gameObject.AddComponent<RegisterUI>();
            Bind(register, "emailInput", registerEmail, "passwordInput", registerPassword,
                "confirmPasswordInput", confirm, "authUIManager", manager, "loginUI", login, "feedbackView", feedback);

            var recovery = Rect("RecoveryForm", frame, 0, 0, 660, 850);
            Header(recovery, "Khôi phục tài khoản", "Quên mật khẩu?", 48);
            Label("Instructions", recovery, "Nhập email đã đăng ký để nhận\nliên kết đặt lại mật khẩu.", 0, 165, 550, 90, 26);
            var recoveryEmail = Input(recovery, "RecoveryEmail", "Email", 48, false);
            Label("SpamHint", recovery, "Vui lòng kiểm tra cả thư mục spam.", 0, -176, 550, 50, 23);

            var navigation = forms.gameObject.AddComponent<ImprovedAuthNavigation>();
            Bind(navigation, "manager", manager, "login", login, "feedback", feedback,
                "loginForm", loginForm.gameObject, "loginMethods", loginMethods.gameObject,
                "registerForm", registerForm.gameObject, "recoveryForm", recovery.gameObject,
                "loginEmail", email, "registerEmail", registerEmail, "recoveryEmail", recoveryEmail);
            Link(loginForm, "ForgotPassword", "Quên mật khẩu?", 160, -27, 240, 46, 24, navigation.ShowRecovery, Cream);
            Button loginButton = Button(loginForm, "LoginButton", "Đăng nhập", -117, login.OnLoginButtonClicked);
            var googleButton = Button(loginMethods, "GoogleButton", "Đăng nhập với Google", -219, google.OnGoogleLoginClicked, true);
            var googleText = googleButton.GetComponentInChildren<TextMeshProUGUI>();
            googleText.rectTransform.anchoredPosition = new Vector2(35, 0);
            googleText.rectTransform.sizeDelta = new Vector2(440, 66);
            googleText.fontSize = 29;
            Picture("GoogleIcon", googleButton.transform, LoadSprite("google"), -205, 0, 44, 44, Color.white);
            Footer(loginMethods, "Chưa có tài khoản?", "Tạo tài khoản", navigation.ShowRegister);
            Button registerButton = Button(registerForm, "CreateAccount", "Tạo tài khoản", -175, register.OnRegisterButtonClicked);
            Footer(registerForm, "Đã có tài khoản?", "Đăng nhập", navigation.ShowLogin);
            var recoveryButton = Button(recovery, "SendRecovery", "Gửi liên kết", -117, navigation.SendRecovery);
            Line(recovery, "FooterRule", 0, -310, 490, 1, Cream * new Color(1,1,1,.6f));
            Link(recovery, "BackToLogin", "←  Quay lại đăng nhập", 0, -365, 540, 56, 30, navigation.ShowLogin, Accent);
            Order(email, password, loginButton);
            Order(registerEmail, registerPassword, confirm, registerButton);
            Order(recoveryEmail, recoveryButton);

            var loading = Rect("LoadingOverlay", forms, 0, 0, 1920, 1080);
            var blocker = Picture("Blocker", loading, null, 0, 0, 1920, 1080, new Color(.04f,.035f,.05f,.7f));
            blocker.raycastTarget = true;
            Picture("LoadingCard", loading, fill, 0, 0, 700, 180, Hex("271E24"));
            var loadingLabel = Label("LoadingLabel", loading, "Đang đăng nhập...", 0, 0, 640, 110, 30);
            Bind(manager, "loginMethod", loginMethods.gameObject, "loginForm", loginForm.gameObject,
                "registerForm", registerForm.gameObject, "formsCanvasGroup", group,
                "loadingOverlay", loading.gameObject, "loadingLabel", loadingLabel);
            loading.gameObject.SetActive(false);
            registerForm.gameObject.SetActive(false);
            recovery.gameObject.SetActive(false);

            ConfigureFeedback(feedback);
            var audio = Find<RectTransform>("AudioControls");
            audio.SetParent(design, false);
            Place(audio, 0, 0, 1920, 1080);
            Place(Find<RectTransform>("MusicToggle"), 790, 468, 70, 70);
            Place(Find<RectTransform>("SoundToggle"), 880, 468, 70, 70);
            feedback.transform.SetAsLastSibling();
            PolishLayout();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            ValidateAndCapture();
            Debug.Log("[ImproveLogin] Built and validated " + ScenePath);
        }

        private static void PrepareAssets()
        {
            foreach (string path in Directory.GetFiles(Root + "/Sprites", "*.png")) ImportSprite(path);
            fill = Rounded("rounded-fill", false);
            outline = Rounded("rounded-outline", true);
            var gradient = new Texture2D(256, 2, TextureFormat.RGBA32, false);
            for (int x=0;x<256;x++) for(int y=0;y<2;y++)
                gradient.SetPixel(x,y,new Color(.075f,.07f,.09f, .96f*(1-Mathf.SmoothStep(0f,1f,Mathf.InverseLerp(.55f,1f,x/255f)))));
            File.WriteAllBytes(Root + "/Sprites/left-shade.png", gradient.EncodeToPNG());
            Object.DestroyImmediate(gradient);
            ImportSprite(Root + "/Sprites/left-shade.png");
            regular = Font("Regular"); semibold = Font("SemiBold");
            alphaTint = new Material(Shader.Find("KLTN/UI/Alpha Tint"));
            AssetDatabase.CreateAsset(alphaTint, Root + "/AlphaTint.mat");
        }

        private static TMP_FontAsset Font(string weight)
        {
            string path = Root + "/Fonts/BeVietnamPro-" + weight + " SDF.asset";
            var existing = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
            if (existing != null) return existing;
            var font = TMP_FontAsset.CreateFontAsset(AssetDatabase.LoadAssetAtPath<UnityEngine.Font>(Root + "/Fonts/BeVietnamPro-" + weight + ".ttf"),
                64, 7, GlyphRenderMode.SDFAA, 2048, 2048, AtlasPopulationMode.Dynamic, true);
            font.name = "BeVietnamPro-" + weight + " SDF";
            AssetDatabase.CreateAsset(font, path);
            font.TryAddCharacters(Enumerable.Range(32, 351).Concat(Enumerable.Range(0x1EA0,96))
                .Concat(new[]{0x01A0,0x01A1,0x01AF,0x01B0,0x2022,0x2026,0x2190}).Select(c=>(uint)c).ToArray(), out _);
            foreach (Texture2D atlas in font.atlasTextures) { atlas.name = font.name + " Atlas"; AssetDatabase.AddObjectToAsset(atlas, font); }
            font.material.name = font.name + " Material";
            AssetDatabase.AddObjectToAsset(font.material, font);
            EditorUtility.SetDirty(font);
            return font;
        }

        private static Sprite Rounded(string name, bool border, float borderWidth = 3f)
        {
            const int n=128;
            var texture = new Texture2D(n,n,TextureFormat.RGBA32,false);
            for(int y=0;y<n;y++) for(int x=0;x<n;x++)
            {
                var p = new Vector2(Mathf.Abs(x-63.5f),Mathf.Abs(y-63.5f))-new Vector2(32,32);
                float d = new Vector2(Mathf.Max(p.x,0),Mathf.Max(p.y,0)).magnitude + Mathf.Min(Mathf.Max(p.x,p.y),0)-30;
                float a = border ? Mathf.Clamp01(.5f-d)*Mathf.Clamp01(d+borderWidth-.5f) : Mathf.Clamp01(.5f-d);
                texture.SetPixel(x,y,new Color(1,1,1,a));
            }
            string path=Root+"/Sprites/"+name+".png";
            File.WriteAllBytes(path,texture.EncodeToPNG()); Object.DestroyImmediate(texture);
            ImportSprite(path, new Vector4(34,34,34,34));
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        private static void ImportSprite(string path, Vector4 border = default)
        {
            path=path.Replace('\\','/');
            AssetDatabase.ImportAsset(path);
            var importer=(TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType=TextureImporterType.Sprite;
            importer.spriteImportMode=SpriteImportMode.Single;
            importer.alphaIsTransparency=true;
            importer.mipmapEnabled=false;
            importer.textureCompression=TextureImporterCompression.Uncompressed;
            importer.maxTextureSize=2048;
            importer.spriteBorder=border;
            importer.SaveAndReimport();
        }

        private static Sprite SaveSprite(string name, Texture2D texture, Rect region)
        {
            var sprite=Sprite.Create(texture, region, new Vector2(.5f,.5f),100);
            sprite.name=name; AssetDatabase.CreateAsset(sprite, Root+"/Sprites/"+name+".asset"); return sprite;
        }

        private static void ConfigureFeedback(AuthFeedbackView feedback)
        {
            string path=Root+"/AuthNotificationItem.prefab";
            AssetDatabase.CopyAsset("Assets/Prefabs/UI/AuthNotificationItem.prefab",path);
            var prefab=PrefabUtility.LoadPrefabContents(path);
            foreach(var text in prefab.GetComponentsInChildren<TMP_Text>(true))
            { text.font=regular; text.fontSize=25; text.color=Cream; }
            PrefabUtility.SaveAsPrefabAsset(prefab,path);
            PrefabUtility.UnloadPrefabContents(prefab);
            Bind(feedback,"notificationPrefab",AssetDatabase.LoadAssetAtPath<AuthNotificationItem>(path));
            var serialized=new SerializedObject(feedback);
            serialized.FindProperty("displayDuration").floatValue=6f;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            var rect=(RectTransform)feedback.transform;
            rect.anchorMin=rect.anchorMax=new Vector2(.5f,1);
            rect.pivot=new Vector2(.5f,1);
            rect.anchoredPosition=new Vector2(310,-128);
            rect.sizeDelta=new Vector2(900,120);
        }

        private static void Header(Transform parent,string subtitle,string title,float size=60)
        {
            Label("Welcome",parent,subtitle,0,324,400,42,27);
            Line(parent,"WelcomeLineLeft",-230,324,65,2,Red);
            Line(parent,"WelcomeLineRight",230,324,65,2,Red);
            Label("Title",parent,title,0,259,590,90,size,true);
        }

        private static void Footer(Transform parent,string prompt,string link,UnityAction action)
        {
            Label("FooterPrompt",parent,prompt,0,-313,310,42,24);
            Line(parent,"FooterLeft",-210,-313,115,1,Cream*new Color(1,1,1,.6f));
            Line(parent,"FooterRight",210,-313,115,1,Cream*new Color(1,1,1,.6f));
            Link(parent,"FooterLink",link,0,-365,440,56,32,action,Accent);
        }

        private static TMP_InputField Input(Transform parent,string name,string placeholder,float y,bool secret)
        {
            var root=Rect(name,parent,0,y,556,76);
            var surface=Picture("Fill",root,fill,0,0,556,76,new Color(.23f,.20f,.21f,.72f));
            surface.raycastTarget=true;
            Picture("Outline",root,outline,0,0,556,76,Cream*new Color(1,1,1,.8f));
            Picture("Icon",root,LoadSprite(secret?"password":"user"),-232,0,34,34,Cream,true);
            Line(root,"Separator",-188,0,1.5f,34,Cream*new Color(1,1,1,.65f));
            var viewport=Rect("TextArea",root,secret?12:34,0,secret?352:396,62);
            viewport.gameObject.AddComponent<RectMask2D>();
            var text=Label("Text",viewport,"",0,0,viewport.sizeDelta.x,62,28);
            text.alignment=TextAlignmentOptions.MidlineLeft; text.richText=false;
            var hint=Label("Placeholder",viewport,placeholder,0,0,viewport.sizeDelta.x,62,28);
            hint.alignment=TextAlignmentOptions.MidlineLeft;
            hint.color=Cream*new Color(1,1,1,.8f);
            var input=root.gameObject.AddComponent<TMP_InputField>();
            input.textViewport=viewport; input.textComponent=text; input.placeholder=hint; input.targetGraphic=surface;
            input.contentType=secret?TMP_InputField.ContentType.Password:TMP_InputField.ContentType.EmailAddress;
            input.characterLimit=secret?128:254;
            input.lineType=TMP_InputField.LineType.SingleLine;
            input.customCaretColor=true; input.caretColor=Cream; input.caretWidth=2;
            input.selectionColor=new Color(.7f,.22f,.24f,.5f);
            input.colors=Colors();
            if(secret)
            {
                var eye=Rect("VisibilityToggle",root,236,0,64,66);
                var target=Picture("HitArea",eye,null,0,0,64,66,Color.clear); target.raycastTarget=true;
                var icon=Picture("Eye",eye,LoadSprite("hide-password"),0,0,36,36,Cream,true);
                var button=eye.gameObject.AddComponent<Button>(); button.targetGraphic=icon; button.colors=Colors();
                var toggle=eye.gameObject.AddComponent<PasswordVisibilityToggle>();
                Bind(toggle,"input",input,"icon",icon,"hiddenIcon",LoadSprite("hide-password"),"visibleIcon",LoadSprite("view-password"));
                UnityEventTools.AddPersistentListener(button.onClick,toggle.Toggle);
                eye.gameObject.AddComponent<UIButtonSound>();
            }
            return input;
        }

        private static Button Button(Transform parent,string name,string text,float y,UnityAction action,bool outlined=false)
        {
            var root=Rect(name,parent,0,y,556,64);
            var image=Picture("Surface",root,fill,0,0,556,64,outlined?new Color(.1f,.08f,.10f,.7f):Red);
            image.raycastTarget=true;
            if(outlined) Picture("Outline",root,outline,0,0,556,64,Cream);
            Label("Label",root,text,0,0,530,56,36,true);
            var button=root.gameObject.AddComponent<Button>(); button.targetGraphic=image; button.colors=Colors();
            UnityEventTools.AddPersistentListener(button.onClick,action);
            root.gameObject.AddComponent<UIButtonSound>();
            return button;
        }

        private static Button Link(Transform parent,string name,string text,float x,float y,float w,float h,float size,UnityAction action,Color color)
        {
            var root=Rect(name,parent,x,y,w,h);
            var hit=Picture("HitArea",root,null,0,0,w,h,Color.clear); hit.raycastTarget=true;
            var label=Label("Label",root,"<u>"+text+"</u>",0,0,w,h,size,true); label.color=color;
            var button=root.gameObject.AddComponent<Button>(); button.targetGraphic=label; button.colors=Colors();
            UnityEventTools.AddPersistentListener(button.onClick,action);
            root.gameObject.AddComponent<UIButtonSound>(); return button;
        }

        private static void Corners(Transform parent)
        {
            var sprite = TightSprite("corner-ornament", "CornerOrnament");
            var material = AssetDatabase.LoadAssetAtPath<Material>(Root + "/AlphaTint.mat");
            for(int i=0;i<4;i++)
            {
                float cornerX=((RectTransform)parent).sizeDelta.x*.5f-51f;
                var corner=Rect("Corner"+i,parent,i%2==0?-cornerX:cornerX,i<2?374:-374,64,64);
                corner.localScale=new Vector3(i%2==0?-1:1,i<2?1:-1,1);
                var ornament=Picture("ExtractedOrnament",corner,sprite,0,0,64,64,Pastel);
                ornament.material=material;
            }
        }

        private static Sprite TightSprite(string source, string name)
        {
            string assetPath=Root+"/Sprites/"+name+".asset";
            var existing=AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
            if(existing!=null) return existing;
            string path=Root+"/Sprites/"+source+".png";
            ImportSprite(path);
            var importer=(TextureImporter)AssetImporter.GetAtPath(path);
            importer.isReadable=true; importer.SaveAndReimport();
            var texture=AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            var pixels=texture.GetPixels32();
            int minX=texture.width,minY=texture.height,maxX=0,maxY=0;
            for(int y=0;y<texture.height;y++) for(int x=0;x<texture.width;x++)
                if(pixels[y*texture.width+x].a>100)
                {minX=Math.Min(x,minX);minY=Math.Min(y,minY);maxX=Math.Max(x,maxX);maxY=Math.Max(y,maxY);}
            return SaveSprite(name,texture,new Rect(minX,minY,maxX-minX+1,maxY-minY+1));
        }

        private static void Order(params Selectable[] fields)
        {
            for(int i=0;i<fields.Length;i++) fields[i].navigation=new Navigation {
                mode=Navigation.Mode.Explicit,selectOnUp=fields[(i+fields.Length-1)%fields.Length],
                selectOnDown=fields[(i+1)%fields.Length] };
        }

        private static TextMeshProUGUI Label(string name,Transform parent,string text,float x,float y,float w,float h,float size,bool bold=false)
        {
            var label=Rect(name,parent,x,y,w,h).gameObject.AddComponent<TextMeshProUGUI>();
            label.font=bold?semibold:regular; label.text=text; label.fontSize=size;
            label.color=Cream; label.alignment=TextAlignmentOptions.Center;
            label.textWrappingMode=TextWrappingModes.Normal; label.raycastTarget=false;
            return label;
        }

        private static Image Picture(string name,Transform parent,Sprite sprite,float x,float y,float w,float h,Color color,bool alphaOnly=false)
        {
            var image=Rect(name,parent,x,y,w,h).gameObject.AddComponent<Image>();
            image.sprite=sprite; image.color=color; image.raycastTarget=false;
            image.type=sprite!=null && sprite.border!=Vector4.zero?Image.Type.Sliced:Image.Type.Simple;
            image.preserveAspect=image.type==Image.Type.Simple && sprite!=null;
            if(alphaOnly) image.material=alphaTint;
            return image;
        }

        private static void Line(Transform parent,string name,float x,float y,float w,float h,Color color) => Picture(name,parent,null,x,y,w,h,color);
        private static RectTransform Rect(string name,Transform parent,float x,float y,float w,float h)
        {
            var rect=new GameObject(name,typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent,false); Place(rect,x,y,w,h); return rect;
        }
        private static void Place(RectTransform rect,float x,float y,float w,float h)
        {
            rect.anchorMin=rect.anchorMax=rect.pivot=new Vector2(.5f,.5f);
            rect.anchoredPosition=new Vector2(x,y); rect.sizeDelta=new Vector2(w,h); rect.localScale=Vector3.one;
        }
        private static ColorBlock Colors() => new ColorBlock {normalColor=Color.white,highlightedColor=new Color(1.2f,1.12f,1.08f),
            selectedColor=new Color(1.15f,1.1f,1.05f),pressedColor=new Color(.8f,.75f,.75f),disabledColor=new Color(.6f,.6f,.6f,.6f),colorMultiplier=1,fadeDuration=.12f};
        private static Color Hex(string hex) { ColorUtility.TryParseHtmlString("#"+hex,out var color); return color; }
        private static Sprite LoadSprite(string name)=>AssetDatabase.LoadAssetAtPath<Sprite>(Root+"/Sprites/"+name+".png");
        private static T Find<T>(string name) where T:Component => Object.FindObjectsByType<T>(FindObjectsInactive.Include)
            .First(t=>t.name==name && t.gameObject.scene==SceneManager.GetActiveScene());
        private static void Bind(Object target,params object[] fields)
        {
            var so=new SerializedObject(target);
            for(int i=0;i<fields.Length;i+=2) so.FindProperty((string)fields[i]).objectReferenceValue=(Object)fields[i+1];
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        [MenuItem("Tools/KLTN/Validate ImproveLoginScene")]
        public static void ValidateAndCapture()
        {
            if(SceneManager.GetActiveScene().path!=ScenePath) EditorSceneManager.OpenScene(ScenePath);
            var nav=Find<ImprovedAuthNavigation>("FormsRoot");
            var manager=Find<AuthUIManager>("UIManager");
            nav.ShowRecovery();
            Require(Find<RectTransform>("RecoveryForm").gameObject.activeSelf,"Recovery did not open");
            Require(!Find<RectTransform>("LoginForm").gameObject.activeSelf,"Login overlaps recovery");
            manager.TryBeginOperation("Validation"); nav.ShowRegister();
            Require(Find<RectTransform>("RecoveryForm").gameObject.activeSelf,"Busy navigation not locked");
            manager.EndOperation();
            Capture("recovery",1920,1080);
            nav.ShowRegister(); Capture("register",1920,1080);
            Require(Find<RectTransform>("RegisterForm").gameObject.activeSelf,"Registration did not open");
            var password=Find<TMP_InputField>("RegisterPassword"); password.text="test-secret";
            var toggle=password.GetComponentInChildren<PasswordVisibilityToggle>();
            toggle.Toggle(); Require(password.inputType==TMP_InputField.InputType.Standard,"Reveal failed");
            toggle.Toggle(); Require(password.inputType==TMP_InputField.InputType.Password && password.text=="test-secret","Hide changed password");
            password.text="";
            nav.ShowLogin(); Capture("login",1920,1080); Capture("login-1366",1366,768); Capture("login-16x10",1920,1200);
            Require(!Find<RectTransform>("RegisterForm").gameObject.activeSelf && !Find<RectTransform>("RecoveryForm").gameObject.activeSelf,"Panels overlap");
            Require(Object.FindObjectsByType<AudioMuteButton>(FindObjectsInactive.Include).Length==2,"Missing audio controls");
            foreach(var input in Object.FindObjectsByType<TMP_InputField>(FindObjectsInactive.Include))
                Require(input.textComponent!=null && input.placeholder!=null,"Input binding missing");
            foreach(var button in Object.FindObjectsByType<Button>(FindObjectsInactive.Include))
                for(int i=0;i<button.onClick.GetPersistentEventCount();i++) Require(button.onClick.GetPersistentTarget(i)!=null,"Broken button: "+button.name);
            Debug.Log("[ImproveLogin] Validation passed: panel navigation, busy lock, password visibility, button bindings, input references, audio controls. Previews: Logs/ImproveLogin.");
        }

        public static void PolishAndValidate()
        {
            EditorSceneManager.OpenScene(ScenePath);
            PolishLayout();
            EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
            AssetDatabase.SaveAssets();
            ValidateAndCapture();
        }

        private static void PolishLayout()
        {
            var canvas=Find<Canvas>("Canvas");
            var backdrop=canvas.transform.Find("LetterboxBackground") as RectTransform;
            if(backdrop==null)
                backdrop=Picture("LetterboxBackground",canvas.transform,null,0,0,1920,1080,Hex("131218")).rectTransform;
            backdrop.anchorMin=Vector2.zero; backdrop.anchorMax=Vector2.one;
            backdrop.offsetMin=backdrop.offsetMax=Vector2.zero; backdrop.SetAsFirstSibling();
            var gradient=new Texture2D(256,2,TextureFormat.RGBA32,false);
            for(int x=0;x<256;x++) for(int y=0;y<2;y++)
                gradient.SetPixel(x,y,new Color(.075f,.07f,.09f,.97f*(1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(.6f,1f,x/255f)))));
            File.WriteAllBytes(Root+"/Sprites/left-shade.png",gradient.EncodeToPNG());
            Object.DestroyImmediate(gradient); ImportSprite(Root+"/Sprites/left-shade.png");
            var shade=Find<Image>("LeftShade"); shade.preserveAspect=false;
            Find<Image>("PanelFill").color=new Color(.12f,.09f,.105f,.98f);
            // Tight sprite geometry around the generated lotus, preserving its original pixels.
            string lotusPath=Root+"/Sprites/lotus.png";
            var importer=(TextureImporter)AssetImporter.GetAtPath(lotusPath);
            importer.isReadable=true; importer.SaveAndReimport();
            var texture=AssetDatabase.LoadAssetAtPath<Texture2D>(lotusPath);
            var pixels=texture.GetPixels32();
            int minX=texture.width,minY=texture.height,maxX=0,maxY=0;
            for(int y=0;y<texture.height;y++) for(int x=0;x<texture.width;x++)
                if(pixels[y*texture.width+x].a>100) {minX=Math.Min(x,minX);minY=Math.Min(y,minY);maxX=Math.Max(x,maxX);maxY=Math.Max(y,maxY);}
            var lotus=AssetDatabase.LoadAssetAtPath<Sprite>(Root+"/Sprites/LotusEmblem.asset");
            if(lotus==null) lotus=SaveSprite("LotusEmblem",texture,new Rect(minX,minY,maxX-minX+1,maxY-minY+1));
            Find<Image>("Lotus").sprite=lotus;
            Place(Find<RectTransform>("Lotus"),0,377,73,50);
            foreach(var input in Object.FindObjectsByType<TMP_InputField>(FindObjectsInactive.Include))
                foreach(var image in input.GetComponentsInChildren<Image>(true))
                    if(image.type==Image.Type.Sliced) image.pixelsPerUnitMultiplier=1.6f;
            foreach(var button in Object.FindObjectsByType<Button>(FindObjectsInactive.Include))
            {
                foreach(var image in button.GetComponentsInChildren<Image>(true))
                    if(image.type==Image.Type.Sliced) image.pixelsPerUnitMultiplier=1.4f;
                var sound=button.GetComponent<UIButtonSound>();
                if(sound!=null && !Enumerable.Range(0,button.onClick.GetPersistentEventCount()).Any(i=>button.onClick.GetPersistentMethodName(i)==nameof(UIButtonSound.PlayClick)))
                    UnityEventTools.AddPersistentListener(button.onClick,sound.PlayClick);
            }
            ApplyThemeVisuals();
        }

        [MenuItem("Tools/KLTN/Apply Pastel Login Theme", false, 1)]
        public static void ApplyPastelTheme()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Apply the theme in Edit Mode.");
            var previous=SceneManager.GetActiveScene();
            var scene=SceneManager.GetSceneByPath(ScenePath);
            bool opened=!scene.isLoaded;
            if(opened) scene=EditorSceneManager.OpenScene(ScenePath,OpenSceneMode.Additive);
            SceneManager.SetActiveScene(scene);
            try
            {
                ApplyThemeVisuals();
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                AssetDatabase.SaveAssets();
                ValidateAndCapture();
                Debug.Log("[ImproveLogin] Pastel theme and extracted corner ornaments saved.");
            }
            finally
            {
                if(opened) EditorSceneManager.CloseScene(scene,true);
                if(previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
            }
        }

        private static void ApplyThemeVisuals()
        {
            var frame=Find<RectTransform>("AuthFrame");
            for(int i=0;i<4;i++)
            {
                var old=frame.Find("Corner"+i);
                if(old!=null) Object.DestroyImmediate(old.gameObject);
            }
            Corners(frame);
            Find<Image>("PanelBorder").color=Pastel;
            // Separate frame outline adds 2 px; input and Google outlines keep their original width.
            var panelOutline=AssetDatabase.LoadAssetAtPath<Sprite>(Root+"/Sprites/panel-outline.png");
            if(panelOutline==null) panelOutline=Rounded("panel-outline",true,5f);
            Find<Image>("PanelBorder").sprite=panelOutline;
            Find<Image>("Lotus").color=Pastel;
            foreach(var image in frame.GetComponentsInChildren<Image>(true))
                if(image.name=="WelcomeLineLeft" || image.name=="WelcomeLineRight") image.color=Pastel;
            foreach(string name in new[]{"LoginButton","CreateAccount","SendRecovery"})
            {
                var button=Find<Button>(name);
                button.transform.Find("Surface").GetComponent<Image>().color=Pastel;
                button.GetComponentInChildren<TMP_Text>(true).color=Hex("2C1D25");
            }
            foreach(var button in frame.GetComponentsInChildren<Button>(true))
                if(button.name=="FooterLink" || button.name=="BackToLogin")
                    button.GetComponentInChildren<TMP_Text>(true).color=Accent;
            ApplyCompactLayout(frame);
            ConfigureButtonDetails();
        }

        private static void ConfigureButtonDetails()
        {
            foreach(string name in new[]{"LoginButton","GoogleButton","CreateAccount","SendRecovery"})
            {
                var button=Find<Button>(name);
                foreach(var rect in button.GetComponentsInChildren<RectTransform>(true))
                {
                    var size=rect.sizeDelta;
                    if(rect==button.transform || rect.name=="Surface" || rect.name=="Outline") size.y=64;
                    else if(rect.name=="Label") size.y=56;
                    rect.sizeDelta=size;
                }
            }
            var back=Find<Button>("BackToLogin");
            var label=back.GetComponentInChildren<TMP_Text>(true);
            label.text="<u>Quay lại đăng nhập</u>";
            Place(label.rectTransform,20,0,280,56);
            var arrow=back.transform.Find("BackArrow");
            if(arrow==null)
                arrow=Picture("BackArrow",back.transform,LoadSprite("back-arrow"),-132,0,32,32,Accent).transform;
            var image=arrow.GetComponent<Image>();
            image.material=AssetDatabase.LoadAssetAtPath<Material>(Root+"/AlphaTint.mat");
            image.color=Accent;
            image.raycastTarget=false;
        }

        private static void ApplyCompactLayout(RectTransform frame)
        {
            CompactToWidth(frame,610f);
            CompactToWidth(frame,564f);
        }

        private static void CompactToWidth(RectTransform frame,float width)
        {
            float reduction=frame.sizeDelta.x-width;
            if(reduction<=0f) return;
            float ratio=(frame.sizeDelta.x-104f-reduction)/(frame.sizeDelta.x-104f);
            foreach(var rect in frame.GetComponentsInChildren<RectTransform>(true))
            {
                var size=rect.sizeDelta;
                if(size.x>=300f) size.x-=reduction;
                else if(rect.name.StartsWith("WelcomeLine") || rect.name=="FooterLeft" || rect.name=="FooterRight") size.x*=ratio;
                rect.sizeDelta=size;
                if(rect==frame) continue;
                var position=rect.anchoredPosition;
                if(Mathf.Abs(position.x)>100f)
                {
                    bool edge=rect.name.StartsWith("Corner") || rect.name=="Icon" || rect.name=="Separator"
                        || rect.name=="VisibilityToggle" || rect.name=="GoogleIcon" || rect.name=="ForgotPassword";
                    position.x=edge ? position.x-Mathf.Sign(position.x)*reduction*.5f : position.x*ratio;
                    rect.anchoredPosition=position;
                }
            }
            foreach(var label in frame.GetComponentsInChildren<TMP_Text>(true)) label.fontSize-=3f;
        }

        private static void Capture(string name,int width,int height)
        {
            var canvas=Find<Canvas>("Canvas");
            var camera=Find<Camera>("Main Camera");
            var oldMode=canvas.renderMode; var oldCamera=canvas.worldCamera;
            var rt=new RenderTexture(width,height,24);
            camera.targetTexture=rt; canvas.renderMode=RenderMode.ScreenSpaceCamera; canvas.worldCamera=camera; canvas.planeDistance=1;
            foreach(var text in canvas.GetComponentsInChildren<TMP_Text>()) text.ForceMeshUpdate();
            Canvas.ForceUpdateCanvases(); camera.Render();
            var previous=RenderTexture.active; RenderTexture.active=rt;
            var image=new Texture2D(width,height,TextureFormat.RGB24,false); image.ReadPixels(new Rect(0,0,width,height),0,0); image.Apply();
            Directory.CreateDirectory("Logs/ImproveLogin"); File.WriteAllBytes("Logs/ImproveLogin/"+name+".png",image.EncodeToPNG());
            RenderTexture.active=previous; camera.targetTexture=null; canvas.renderMode=oldMode; canvas.worldCamera=oldCamera;
            Object.DestroyImmediate(image); rt.Release(); Object.DestroyImmediate(rt);
        }
        private static void Require(bool value,string message) { if(!value) throw new InvalidOperationException(message); }
    }
}
#endif
