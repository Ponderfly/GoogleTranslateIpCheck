package main

import (
	"bytes"
	"encoding/json"
	"fmt"
	"io"
	"io/ioutil"
	"math/rand"
	"os"
	"path"
	"strings"
)

// Gop
func readJsonConfig(filename string, config interface{}) error {
	fileext := path.Ext(filename)
	filename1 := strings.TrimSuffix(filename, fileext) + ".user" + fileext

	cm := make(map[string]interface{})
	for i, name := range []string{filename, filename1} {
		f, err := os.Open(name)
		if err != nil {
			if i == 0 {
				return err
			} else {
				continue
			}
		}
		defer f.Close()

		data, err := readJson(f)
		if err != nil {
			return err
		}

		data = bytes.TrimPrefix(data, []byte("\xef\xbb\xbf"))

		cm1 := make(map[string]interface{})

		d := json.NewDecoder(bytes.NewReader(data))
		d.UseNumber()

		if err = d.Decode(&cm1); err != nil {
			return err
		}

		if err = mergeMap(cm, cm1); err != nil {
			return err
		}
	}

	data, err := json.Marshal(cm)
	if err != nil {
		return err
	}

	d := json.NewDecoder(bytes.NewReader(data))
	d.UseNumber()

	return d.Decode(config)
}

func readJson(r io.Reader) ([]byte, error) {
	s, err := ioutil.ReadAll(r)
	if err != nil {
		return s, err
	}

	lines := make([]string, 0)
	for _, line := range strings.Split(strings.Replace(string(s), "\r\n", "\n", -1), "\n") {
		line = strings.TrimSpace(line)
		if line == "" || strings.HasPrefix(line, "//") {
			continue
		}
		lines = append(lines, line)
	}

	var b bytes.Buffer
	for i, line := range lines {
		if i < len(lines)-1 {
			nextLine := strings.TrimSpace(lines[i+1])
			if nextLine == "]" ||
				nextLine == "]," ||
				nextLine == "}" ||
				nextLine == "}," {
				if strings.HasSuffix(line, ",") {
					line = strings.TrimSuffix(line, ",")
				}
			}
		}
		b.WriteString(line)
	}

	return b.Bytes(), nil
}

func mergeMap(m1 map[string]interface{}, m2 map[string]interface{}) error {
	for key, value := range m2 {

		m1v, m1_has_key := m1[key]
		m2v, m2v_is_map := value.(map[string]interface{})
		m1v1, m1v_is_map := m1v.(map[string]interface{})

		switch {
		case !m1_has_key, !m2v_is_map:
			m1[key] = value
		case !m1v_is_map:
			return fmt.Errorf("m1v=%#v is not a map, but m2v=%#v is a map", m1v, m2v)
		default:
			mergeMap(m1v1, m2v)
		}
	}

	return nil
}

func randInt(l, u int) int {
	return rand.Intn(u-l) + l
}

// llm.xadl
// unupk.bfrf.pvi
func randomHost() string {
	a := make([][]byte, randInt(2, 4))
	for i := range a {
		m := randInt(3, 7)
		b := make([]byte, m)
		for j := 0; j < m; j++ {
			b[j] = byte(randInt(97, 122))
		}
		a[i] = b
	}
	return string(bytes.Join(a, []byte{46}))
}
