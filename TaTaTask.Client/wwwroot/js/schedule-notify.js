// 到点提醒：浏览器系统通知。
// 需要安全上下文（HTTPS 或 localhost），否则浏览器会拒绝；未授权时由页面内通栏兜底。

export function isSupported() {
    return typeof Notification !== 'undefined';
}

export function isGranted() {
    return typeof Notification !== 'undefined' && Notification.permission === 'granted';
}

export function requestPermission() {
    if (typeof Notification === 'undefined') {
        return Promise.resolve('unsupported');
    }
    if (Notification.permission === 'granted') {
        return Promise.resolve('granted');
    }
    return Notification.requestPermission();
}

export function notify(title, body) {
    try {
        if (typeof Notification !== 'undefined' && Notification.permission === 'granted') {
            new Notification(title, { body: body, tag: 'tatatask-schedule' });
            return true;
        }
    } catch (e) {
        // 忽略：通知失败不影响页面
    }
    return false;
}
