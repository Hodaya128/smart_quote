const apiRoot = BASE_URL + '/api/users';
const headers = { 'X-Token': 'your-token-here' };

const setLoading = (on) => {
    $('#saveBtn').toggle(!on);
    $('#loader').toggleClass('active', on);
};

$('#addUserForm').submit(e => {
    e.preventDefault();
    setLoading(true);

    const body = {
        email: $('#email').val(),
        type: $('#type').val(),
        password: $('#password').val()
    };

    $.ajax({
        url: apiRoot + '/register',
        method: 'POST',
        contentType: 'application/json',
        headers: headers,
        data: JSON.stringify(body),
        success: () => window.location.href = '/pages/users',
        error: (xhr, status, err) => {
            console.log('Error:', status, err);
            alert('שגיאה בשמירה, נסה שוב');
            setLoading(false);
        }
    });
});