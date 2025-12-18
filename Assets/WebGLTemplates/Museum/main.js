(function () {
    function playCSSAnimation(elementId, animationName, duration = 6000, timingFunction = 'linear', onComplete = null) {
        const element = document.getElementById(elementId);
        if (!element) {
            console.error(`封面元素ID "${elementId}" 不存在`);
            return;
        }
        element.style.animation = `${animationName} ${duration}ms ${timingFunction} forwards`;
        const handleAnimationEnd = () => {
            element.removeEventListener('animationend', handleAnimationEnd);
            if (typeof onComplete === 'function') {
                onComplete();
            }
        };
        element.addEventListener('animationend', handleAnimationEnd);
        element.style.animation = 'none';
        element.offsetHeight;
        element.style.animation = `${animationName} ${duration}ms ${timingFunction} forwards`;
    }

    var audioContext = null;
    var unmutePrompt = document.getElementById('audio-unmute-prompt');
    var unmuteBtn = document.getElementById('unmute-btn');
    var dismissBtn = document.getElementById('dismiss-btn');
    var hasUserInteracted = false;

    function initAudioContext() {
        try {
            var AudioContext = window.AudioContext || window.webkitAudioContext;
            if (AudioContext) {
                audioContext = new AudioContext();
                console.log('[Audio] 音频上下文已创建');
            }
        } catch (e) {
            console.warn('[Audio] 无法创建音频上下文:', e);
        }
    }

    function unlockAudio() {
        if (!audioContext) {
            initAudioContext();
        }
        if (audioContext && audioContext.state === 'suspended') {
            audioContext.resume().then(function () {
                console.log('[Audio] 音频上下文已激活');
                var oscillator = audioContext.createOscillator();
                var gainNode = audioContext.createGain();
                oscillator.connect(gainNode);
                gainNode.connect(audioContext.destination);
                gainNode.gain.setValueAtTime(0, audioContext.currentTime);
                oscillator.frequency.setValueAtTime(440, audioContext.currentTime);
                oscillator.start(audioContext.currentTime);
                oscillator.stop(audioContext.currentTime + 0.01);
            }).catch(function (err) {
                console.warn('[Audio] 无法激活音频上下文:', err);
            });
        }
    }

    function showUnmutePrompt() {
        if (unmutePrompt && !hasUserInteracted) {
            unmutePrompt.style.display = 'block';
            console.log('[Audio] 显示音频静音提示');
        }
    }

    function hideUnmutePrompt() {
        if (unmutePrompt) {
            unmutePrompt.style.display = 'none';
            console.log('[Audio] 隐藏音频静音提示');
        }
    }

    window.ShowAudioUnmutePrompt = function () {
        showUnmutePrompt();
    };

    function setupUserInteractionListeners() {
        var events = ['click', 'touchstart', 'keydown', 'mousedown'];
        events.forEach(function (event) {
            document.addEventListener(event, function onFirstInteraction() {
                hasUserInteracted = true;
                document.removeEventListener(event, onFirstInteraction);
                console.log('[Audio] 检测到用户交互');
            }, { once: true });
        });

        if (unmuteBtn) {
            unmuteBtn.addEventListener('click', function () {
                unlockAudio();
                hideUnmutePrompt();
                if (window.unityInstance && window.unityInstance.SendMessage) {
                    window.unityInstance.SendMessage('AudioManager', 'OnAudioUnlocked');
                }
            });
        }

        if (dismissBtn) {
            dismissBtn.addEventListener('click', function () {
                hideUnmutePrompt();
                setTimeout(function () {
                    if (!hasUserInteracted) {
                        showUnmutePrompt();
                    }
                }, 30000);
            });
        }
    }

    window.addEventListener('load', function () {
        initAudioContext();
        setupUserInteractionListeners();
        console.log('[Audio] 页面加载完成，音频功能已初始化');

        if ('serviceWorker' in navigator) {
            navigator.serviceWorker.register('ServiceWorker.js').then(function (registration) {
                console.log('[SW] Registered successfully:', registration.scope);
            }).catch(function (error) {
                console.log('[SW] Registration failed:', error);
            });
        } else {
            console.log('[SW] Service Worker not supported');
        }
    });

    var container = document.querySelector('#unity-container');
    var canvas = document.querySelector('#unity-canvas');
    var loadingBar = document.querySelector('#unity-loading-bar');
    var progressBarFull = document.querySelector('#unity-progress-bar-full');
    var warningBanner = document.querySelector('#unity-warning');
    var Custom_Cover = document.querySelector('#Custom_Cover');
    var Tip_FirstLoading = document.querySelector('#Tip_FirstLoading');

    function unityShowBanner(msg, type) {
        function updateBannerVisibility() {
            warningBanner.style.display = warningBanner.children.length ? 'block' : 'none';
        }
        var div = document.createElement('div');
        div.innerHTML = msg;
        warningBanner.appendChild(div);
        if (type === 'error') div.className = 'unity-banner-error';
        else {
            if (type === 'warning') div.className = 'unity-banner-warning';
            setTimeout(function () {
                warningBanner.removeChild(div);
                updateBannerVisibility();
            }, 6000);
        }
        updateBannerVisibility();
    }

    var buildUrl = 'Build';
    var loaderUrl = buildUrl + '/Build.loader.js';
    var config = {
        dataUrl: buildUrl + '/Build.data.unityweb',
        frameworkUrl: buildUrl + '/Build.framework.js.unityweb',
        codeUrl: buildUrl + '/Build.wasm.unityweb',
        streamingAssetsUrl: 'StreamingAssets',
        companyName: 'SoulFlaw',
        productName: 'Museum',
        productVersion: '0.1.0',
        showBanner: unityShowBanner,
    };

    if (/iPhone|iPad|iPod|Android/i.test(navigator.userAgent)) {
        var meta = document.createElement('meta');
        meta.name = 'viewport';
        meta.content = 'width=device-width, height=device-height, initial-scale=1.0, user-scalable=no, shrink-to-fit=yes';
        document.getElementsByTagName('head')[0].appendChild(meta);
    }

    loadingBar.style.display = 'block';

    window.OpenFullscreen = function (elementId) {
        var target = elementId ? document.getElementById(elementId) : document.documentElement;
        if (!target) {
            console.warn('[Fullscreen] 无法找到元素：' + elementId);
            return;
        }

        var requestFullscreen = target.requestFullscreen
            || target.webkitRequestFullscreen
            || target.mozRequestFullScreen
            || target.msRequestFullscreen;

        if (requestFullscreen) {
            requestFullscreen.call(target).catch(function (err) {
                console.warn('[Fullscreen] 请求全屏失败：', err);
            });
        } else {
            console.warn('[Fullscreen] 当前浏览器不支持全屏 API');
        }
    };

    window.CloseFullscreen = function () {
        var exitFullscreen = document.exitFullscreen
            || document.webkitExitFullscreen
            || document.mozCancelFullScreen
            || document.msExitFullscreen;

        if (exitFullscreen) {
            exitFullscreen.call(document).catch(function (err) {
                console.warn('[Fullscreen] 退出全屏失败：', err);
            });
        } else {
            console.warn('[Fullscreen] 当前浏览器不支持退出全屏 API');
        }
    };

    window.SwitchFullscreen = function (elementId) {
        var isFullscreen = document.fullscreenElement
            || document.webkitFullscreenElement
            || document.mozFullScreenElement
            || document.msFullscreenElement;

        if (isFullscreen) {
            window.CloseFullscreen();
        } else {
            window.OpenFullscreen(elementId);
        }
    };

    var script = document.createElement('script');
    script.src = loaderUrl;
    script.onload = function () {
        createUnityInstance(canvas, config, function (progress) {
            progressBarFull.style.width = (100 * progress) + '%';
        }).then(function (unityInstance) {
            window.unityInstance = unityInstance;
            loadingBar.style.display = 'none';
            Custom_Cover.style.display = 'block';
            playCSSAnimation('Custom_Cover', 'FadeOut', 5000, 'linear', function () {
                if (Custom_Cover.parentNode) {
                    Custom_Cover.parentNode.removeChild(Custom_Cover);
                    console.log('已成功删除封面元素');
                }
                console.log('[Audio] Unity实例创建完成，等待音频播放请求');
            });
        }).catch(function (message) {
            alert(message);
        });
    };
    document.body.appendChild(script);
})();