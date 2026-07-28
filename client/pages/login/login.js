const server = BASE_URL + "/";

$(document).ready(function () {
    $('#loginForm').submit(function (e) {
        e.preventDefault();
        login();
    });
});

function login() {
    //בונה אלמנט שישמש בתור הbody
    const loginDataBody = {
        email: $('#email').val(),
        password: $('#password').val()
    };

    ajaxCall("POST", server + 'api/users/login', loginDataBody, loginSuccess, loginError);
}

function loginSuccess(user) {
    if (user != null) {
        // שומר בזיכרון את המשתמש
        localStorage.setItem('userLoggedIn', JSON.stringify(user));
        //עובר למסך הבית
        window.location.href = '/pages/home';
    } else {
        alert('Invalid email or password.');
    }
}

function loginError(err) {
    console.error(err);
    alert('Error during login: ' + err.responseText);
}
