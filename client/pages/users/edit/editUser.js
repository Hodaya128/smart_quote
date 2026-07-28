const apiRoot = BASE_URL + '/api/users';
const reqHeaders = { 'X-Token': 'your-token-here' };
let id;

loadUser();

function loadUser() {
    id = new URLSearchParams(window.location.search).get('id');
    if (!id) { alert('מזהה משתמש חסר'); return; }

    $.ajax({
        url: apiRoot + '/' + id,
        method: 'GET',
        dataType: 'json',
        headers: reqHeaders,
        success: function (data) {
            $('#email').val(data.email);
            $('#type').val(data.type);
        },
        error: function (xhr, status, error) {
            console.log('Error loading user:', status, error);
            alert('שגיאה בטעינת המשתמש');
        }
    });
}

const setLoading = (on) => {
    $('#saveBtn').toggle(!on);
    $('#loader').toggleClass('active', on);
};

$('#editUserForm').submit(e => {
    e.preventDefault();
    setLoading(true);

    const password = $('#password').val();

    const updateUser = $.ajax({
        url: apiRoot + '/' + id,
        method: 'PUT',
        contentType: 'application/json',
        headers: reqHeaders,
        data: JSON.stringify({ email: $('#email').val(), type: $('#type').val() })
    });

    const requests = [updateUser];

    if (password) {
        const updatePassword = $.ajax({
            url: apiRoot + '/' + id + '/password',
            method: 'PUT',
            contentType: 'application/json',
            headers: reqHeaders,
            data: JSON.stringify({ password })
        });
        requests.push(updatePassword);
    }

    $.when(...requests)
        .done(() => window.location.href = '/pages/users')
        .fail((xhr, status, err) => {
            console.log('Error updating:', status, err);
            alert('שגיאה בעדכון, נסה שוב');
            setLoading(false);
        });
});