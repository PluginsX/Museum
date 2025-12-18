// CommonFunction.jslib - 通用JS执行函数库
(function () {
    var target = (typeof LibraryManager !== 'undefined' && LibraryManager.library)
        ? LibraryManager.library
        : (typeof globalThis !== 'undefined')
            ? globalThis
            : (typeof self !== 'undefined')
                ? self
                : this;

    mergeInto(target, {
        JS_ExecuteCommand: function (commandPtr) {
            if (typeof window === 'undefined') {
                console.warn('[WebGL] 非浏览器环境，跳过JS命令执行');
                return;
            }

            var command = UTF8ToString(commandPtr || 0);
            if (!command || !command.trim()) {
                console.warn('[WebGL] 空命令，跳过执行');
                return;
            }

            try {
                (new Function(command))();
            } catch (error) {
                console.error('[WebGL] JS命令执行失败:', error);
            }
        }
    });
})();
