    document.getElementById('profile-username').textContent = getUserName();

function logout() {
    localStorage.removeItem('userLoggedIn');
    window.location.href = '/pages/login';
}
