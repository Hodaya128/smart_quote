
init();

function init(){
    isLogging();
}

// בודק האם מחובר
function isLogging() {
    const isLogin = localStorage.getItem('userLoggedIn');
    if (!isLogin) {
        alert('You must login first!');
        window.location.href = window.location.origin + '/pages/login';
    }
}
//מחזיר את השם של מי שמחובר
function getUserName() {
    const user = JSON.parse(localStorage.getItem('userLoggedIn'));
    return user.userName ;
}
// מחזיר את התפקיד של המתחבר
function getUserRole() {
    const user = JSON.parse(localStorage.getItem('userLoggedIn'));
    return user ? user.type : null;
}
// מחזיר האם הוא אדמין או מנהל
function isAdminOrManager() {
    const role = getUserRole();
    return role === 'Admin' || role === 'Manager';
}
// מציג פופאפ ומפנה לדף הבית אם המשתמש אינו מנהל
function requireManager() {
    if (!isAdminOrManager()) {
        const overlay = document.createElement('div');
        overlay.style.cssText = 'position:fixed;top:0;left:0;width:100%;height:100%;background:rgba(0,0,0,0.5);z-index:9999;display:flex;align-items:center;justify-content:center;';
        overlay.innerHTML = `
            <div style="background:#fff;border-radius:12px;padding:40px;text-align:center;max-width:360px;box-shadow:0 8px 32px rgba(0,0,0,0.2);">
                <div style="font-size:48px;margin-bottom:12px;">🔒</div>
                <h2 style="margin:0 0 8px;color:#1a1a2e;font-size:20px;">אין הרשאה</h2>
                <p style="color:#666;margin:0 0 24px;font-size:15px;">דף זה מיועד למנהלים בלבד.</p>
                <button onclick="window.location.href=window.location.origin+'/pages/home'"
                    style="background:#5b7fa6;color:#fff;border:none;border-radius:8px;padding:10px 28px;font-size:15px;cursor:pointer;">
                    חזרה לדף הבית
                </button>
            </div>`;
        document.body.appendChild(overlay);
    }
}

// מחזיר את ה-ID של המשתמש המחובר
function getUserID() {
    const user = JSON.parse(localStorage.getItem('userLoggedIn'));
    return user ? user.userID : null;
}

// התנתקות/ מחיקת קאש
function logout() {
    localStorage.removeItem('userLoggedIn');
    alert('You have been logged out.');
    window.location.href = window.location.origin + '/pages/login';
}
