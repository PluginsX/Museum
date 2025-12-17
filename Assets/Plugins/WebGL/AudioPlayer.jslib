// AudioPlayer.jslib - 最终编译兼容版（解决window/self/LibraryManager未定义问题）
(function () {
    // 在构建阶段（Node 环境）时，LibraryManager 会存在；运行阶段则使用globalThis/window
    var target = (typeof LibraryManager !== 'undefined' && LibraryManager.library)
        ? LibraryManager.library
        : (typeof globalThis !== 'undefined')
            ? globalThis
            : (typeof self !== 'undefined')
                ? self
                : this;

    mergeInto(target, {
        // 播放音频：JS_PlayAudio（对应C#的JS_PlayAudio）
        JS_PlayAudio: function (path, loop, audioKey) {
            // 1. 仅在浏览器环境中执行（排除Node.js编译环境）
            if (typeof window === 'undefined') {
                console.warn('[Audio] 非浏览器环境，跳过音频播放');
                return;
            }

            // 2. 初始化全局音频实例存储对象（仅在第一次调用时初始化）
            if (!window.audioInstances) {
                window.audioInstances = {};
            }

            // 3. 替换废弃的Pointer_stringify为UTF8ToString
            const audioPath = UTF8ToString(path);
            const audioKeyStr = UTF8ToString(audioKey);
            const isLoop = loop === 1;

            // 4. 资源名合法性校验：自动补全.mp3后缀（支持mp3/wav/ogg）
            let finalAudioPath = audioPath;
            const validExtensions = ['.mp3', '.wav', '.ogg'];
            const hasValidExt = validExtensions.some(ext => finalAudioPath.endsWith(ext));
            if (!hasValidExt) {
                finalAudioPath += '.mp3'; // 默认补全为mp3
                console.warn(`[Audio] 音频资源名缺少后缀，自动补全为：${finalAudioPath}`);
            }

            // 5. 获取/创建音频实例（使用window全局的audioInstances）
            let audio = window.audioInstances[audioKeyStr];
            if (!audio) {
                audio = new Audio(finalAudioPath);
                window.audioInstances[audioKeyStr] = audio;

                // 监听播放完成事件
                audio.onended = function () {
                    if (window.onAudioEnded) {
                        window.onAudioEnded(audioKeyStr);
                    }
                };
            }

            // 监听音频加载错误
            audio.onerror = function () {
                console.error(`[Audio] 音频加载失败：${finalAudioPath}（请检查资源路径或文件名）`);
            };

            // 设置循环并播放（处理浏览器自动播放策略限制）
            audio.loop = isLoop;
            audio.play().catch(function (err) {
                console.warn(`[Audio] 音频自动播放失败（浏览器策略限制，需用户交互触发）：${err.message}`);
            });
        },

        // 暂停音频：JS_PauseAudio
        JS_PauseAudio: function (audioKey) {
            if (typeof window === 'undefined' || !window.audioInstances) {
                return;
            }

            const audioKeyStr = UTF8ToString(audioKey);
            const audio = window.audioInstances[audioKeyStr];
            if (audio && !audio.paused) {
                audio.pause();
            }
        },

        // 停止音频：JS_StopAudio
        JS_StopAudio: function (audioKey) {
            if (typeof window === 'undefined' || !window.audioInstances) {
                return;
            }

            const audioKeyStr = UTF8ToString(audioKey);
            const audio = window.audioInstances[audioKeyStr];
            if (audio) {
                audio.pause();
                audio.currentTime = 0; // 重置到音频开头
            }
        },

        // 销毁音频实例：JS_DestroyAudio
        JS_DestroyAudio: function (audioKey) {
            if (typeof window === 'undefined' || !window.audioInstances) {
                return;
            }

            const audioKeyStr = UTF8ToString(audioKey);
            const audio = window.audioInstances[audioKeyStr];
            if (audio) {
                audio.pause();
                audio.currentTime = 0;
                delete window.audioInstances[audioKeyStr]; // 从全局对象中移除
            }
        },

        // 获取音频播放进度（0~1）：JS_GetAudioProgress
        JS_GetAudioProgress: function (audioKey) {
            if (typeof window === 'undefined' || !window.audioInstances) {
                return 0;
            }

            const audioKeyStr = UTF8ToString(audioKey);
            const audio = window.audioInstances[audioKeyStr];
            if (audio && audio.duration) {
                return audio.currentTime / audio.duration;
            }
            return 0; // 无音频或未加载完成时返回0
        },

        // 设置音频音量（0~1）：JS_SetAudioVolume
        JS_SetAudioVolume: function (audioKey, volume) {
            if (typeof window === 'undefined' || !window.audioInstances) {
                return;
            }

            const audioKeyStr = UTF8ToString(audioKey);
            const audio = window.audioInstances[audioKeyStr];
            if (audio) {
                audio.volume = volume; // 浏览器中volume范围0~1
            }
        },

        // 获取当前音量（0~1）：JS_GetAudioVolume
        JS_GetAudioVolume: function (audioKey) {
            if (typeof window === 'undefined' || !window.audioInstances) {
                return 1;
            }

            const audioKeyStr = UTF8ToString(audioKey);
            const audio = window.audioInstances[audioKeyStr];
            if (audio) {
                return audio.volume;
            }
            return 1; // 无音频时默认返回最大音量
        },

        // 跳转音频进度（0~1）：JS_SeekAudio
        JS_SeekAudio: function (audioKey, progress) {
            if (typeof window === 'undefined' || !window.audioInstances) {
                return;
            }

            const audioKeyStr = UTF8ToString(audioKey);
            const audio = window.audioInstances[audioKeyStr];
            if (audio && audio.duration) {
                // 限制进度在0~1范围内
                const clampedProgress = Math.max(0, Math.min(1, progress));
                audio.currentTime = clampedProgress * audio.duration;
            }
        }
    });
})();